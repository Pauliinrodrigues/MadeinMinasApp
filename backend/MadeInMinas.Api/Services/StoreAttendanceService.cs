using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.StoreAttendance;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MadeInMinas.Api.Services;

public sealed class StoreAttendanceService(AppDbContext database, IOptionsSnapshot<StoreAttendanceOptions> defaults,
    TimeProvider clock, ILogger<StoreAttendanceService> logger)
{
    private const long AttendanceLock = 720264009001;
    private static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public async Task<StoreAttendanceResponse> GetAsync(CancellationToken cancellationToken) =>
        Response(await database.StoreAttendanceSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken));

    public async Task<PublicStoreAttendanceResponse> PublicAsync(CancellationToken cancellationToken) =>
        Evaluate((await GetAsync(cancellationToken)).Settings, clock.GetUtcNow());

    public static PublicStoreAttendanceResponse Evaluate(StoreAttendanceData settings, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, BusinessTimeZone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var day = ((int)date.DayOfWeek + 6) % 7 + 1;
        var hours = settings.Hours.Single(hour => hour.Day == day);
        var open = false;
        DateTimeOffset? next = null;
        if (!hours.IsClosed)
        {
            var start = Instant(date, hours.OpensAt);
            var end = hours.ClosesAt == "00:00" ? Instant(date.AddDays(1), "00:00") : Instant(date, hours.ClosesAt);
            open = now >= start && now < end;
            if (open)
                next = end;
        }
        if (!open)
        {
            for (var offset = 0; offset <= 7; offset++)
            {
                var candidateDate = date.AddDays(offset);
                var candidate = settings.Hours.Single(hour => hour.Day == ((int)candidateDate.DayOfWeek + 6) % 7 + 1);
                if (candidate.IsClosed)
                    continue;
                var start = Instant(candidateDate, candidate.OpensAt);
                if (start > now)
                { next = start; break; }
            }
        }
        return new PublicStoreAttendanceResponse(settings.Hours.OrderBy(hour => hour.Day).ToArray(), settings.PaymentMethods,
            settings.DeliveryEstimateMessage, BusinessTimeZone.Id, open, settings.IsPaused!.Value,
            !settings.IsPaused.Value && (open || settings.ReceiveOrdersOutsideHours!.Value), now, next);
    }

    private static DateTimeOffset Instant(DateOnly date, string time) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture)), BusinessTimeZone));

    public Task LockForOrderAsync(CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({AttendanceLock})", cancellationToken);

    public async Task EnsureAcceptingOrdersAsync(CancellationToken cancellationToken)
    {
        var current = await PublicAsync(cancellationToken);
        if (!current.AcceptingOrders)
            throw new OrderException(OrderError.StoreNotAcceptingOrders, current.IsPaused
                ? "Os pedidos estão pausados no momento. Seu carrinho foi preservado; fale com o atendimento."
                : "Estamos fora do horário de funcionamento. Seu carrinho foi preservado; confira os horários para enviar seu pedido.");
    }

    public async Task<StoreAttendanceResponse> SaveAsync(Guid actorId, Guid actorStamp, SaveStoreAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await CatalogWriteTransaction.BeginAsync(database, actorId, actorStamp,
            () => new OrderException(OrderError.InvalidSession, "Sessão inválida. Entre novamente."),
            () => new OrderException(OrderError.PermissionDenied, "Acesso restrito ao administrador."), cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AttendanceLock})", cancellationToken);
        var stored = await database.StoreAttendanceSettings.SingleOrDefaultAsync(cancellationToken);
        var current = Response(stored);
        var settings = Normalize(request.Settings);
        if (stored is not null && Serialize(settings) == Serialize(current.Settings))
        {
            await transaction.CommitAsync(cancellationToken);
            return current;
        }
        if (request.ExpectedRevision != current.Revision)
            throw new OrderException(OrderError.StoreSettingsChanged, "As configurações mudaram. Recarregue e confira antes de salvar.");
        if (stored is null)
        {
            stored = new StoreAttendanceSettings();
            database.StoreAttendanceSettings.Add(stored);
        }
        stored.SettingsJson = Serialize(settings);
        stored.Version++;
        stored.UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        stored.UpdatedById = actorId;
        stored.UpdatedByName = await database.Users.Where(user => user.Id == actorId).Select(user => user.Name).SingleAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Store attendance settings version {Version} updated by {ActorId}.", stored.Version, actorId);
        return Response(stored);
    }

    private StoreAttendanceResponse Response(StoreAttendanceSettings? stored)
    {
        var settings = Normalize(stored is null ? defaults.Value.Data() : JsonSerializer.Deserialize<StoreAttendanceData>(stored.SettingsJson)!);
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{stored?.Version ?? 0}\n{Serialize(settings)}")));
        return new StoreAttendanceResponse(settings, revision, stored?.UpdatedAt, stored?.UpdatedByName);
    }

    private static StoreAttendanceData Normalize(StoreAttendanceData settings) => settings with
    {
        Hours = settings.Hours.OrderBy(hour => hour.Day).ToArray(),
        PaymentMethods = settings.PaymentMethods.Order(StringComparer.Ordinal).ToArray(),
        DeliveryEstimateMessage = settings.DeliveryEstimateMessage.Trim().Normalize()
    };
    private static string Serialize(StoreAttendanceData settings) => JsonSerializer.Serialize(settings);
}
