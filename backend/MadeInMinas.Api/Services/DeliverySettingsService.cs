using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Delivery;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MadeInMinas.Api.Services;

public sealed class DeliverySettingsService(AppDbContext database, IOptionsSnapshot<PublicDeliveryOptions> legacy,
    TimeProvider clock, ILogger<DeliverySettingsService> logger)
{
    // Compartilhado com a criação do pedido: a taxa não muda entre a revisão final e o commit.
    private const long CoverageLock = 720264006001;

    public PublicCheckoutOptionsResponse PublicOptions() => new(legacy.Value.FixedFee, 0,
        string.IsNullOrWhiteSpace(legacy.Value.PickupAddress) ? null : legacy.Value.PickupAddress.Trim().Normalize());

    public Task LockForOrderAsync(CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({CoverageLock})", cancellationToken);

    public async Task<DeliverySettingsResponse> GetAsync(CancellationToken cancellationToken)
    {
        var stored = await database.DeliverySettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return ToResponse(stored);
    }

    public async Task<PublicDeliveryAreaResponse[]> PublicAreasAsync(CancellationToken cancellationToken) =>
        (await GetAsync(cancellationToken)).Areas.Where(area => area.IsActive)
        .OrderBy(area => area.State, StringComparer.Ordinal).ThenBy(area => area.City, StringComparer.Ordinal)
        .ThenBy(area => area.Neighborhood, StringComparer.Ordinal)
        .Select(area => new PublicDeliveryAreaResponse(area.Id, area.Neighborhood, area.City, area.State, area.Fee)
        { CoversAllNeighborhoods = area.CoversAllNeighborhoods }).ToArray();

    public async Task<DeliverySettingsResponse> SaveAsync(Guid actorId, Guid actorStamp, SaveDeliverySettingsRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await CatalogWriteTransaction.BeginAsync(database, actorId, actorStamp,
            () => new DeliverySettingsException(DeliverySettingsError.InvalidSession, "Sessão inválida. Faça login novamente."),
            () => new DeliverySettingsException(DeliverySettingsError.PermissionDenied, "Acesso restrito ao administrador."), cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({CoverageLock})", cancellationToken);
        var stored = await database.DeliverySettings.SingleOrDefaultAsync(cancellationToken);
        var current = ToResponse(stored);
        if (legacy.Value.FixedFee is { } fixedFee && request.Areas.Any(area => area.Fee != fixedFee))
            throw new DeliverySettingsException(DeliverySettingsError.FixedDeliveryFeeRequired,
                "Use a taxa fixa configurada para todas as regiões de entrega.");
        var areas = request.Areas.Select(area => new DeliveryAreaResponse(area.Id, area.Neighborhood.Trim().Normalize(),
            area.City.Trim().Normalize(), area.State, area.Fee!.Value, area.IsActive!.Value)
        { CoversAllNeighborhoods = area.CoversAllNeighborhoods }).OrderBy(area => area.Id, StringComparer.Ordinal).ToArray();
        var json = Serialize(areas);
        // Uma resposta perdida pode ser recuperada sem regravar a mesma alteração.
        if (stored is not null && json == Serialize(current.Areas))
        {
            await transaction.CommitAsync(cancellationToken);
            return current;
        }
        if (request.ExpectedRevision != current.Revision)
            throw new DeliverySettingsException(DeliverySettingsError.DeliverySettingsChanged, "As regiões foram alteradas. Recarregue e confira os dados antes de salvar.");
        if (current.Areas.Any(area => !areas.Any(updated => updated.Id == area.Id)))
            throw new DeliverySettingsException(DeliverySettingsError.DeliveryAreaRemovalDenied, "Pause a região em vez de removê-la da lista.");
        if (stored is null)
        {
            stored = new DeliverySettings();
            database.DeliverySettings.Add(stored);
        }
        stored.AreasJson = json;
        stored.Version++;
        stored.UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        stored.UpdatedById = actorId;
        stored.UpdatedByName = await database.Users.Where(user => user.Id == actorId).Select(user => user.Name).SingleAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Delivery settings version {Version} updated by {ActorId}; {ActiveCount} active areas.",
            stored.Version, actorId, areas.Count(area => area.IsActive));
        return ToResponse(stored);
    }

    private DeliverySettingsResponse ToResponse(DeliverySettings? stored)
    {
        var areas = stored is null
            ? legacy.Value.Areas.Select(area => new DeliveryAreaResponse(area.Id, area.Neighborhood.Trim().Normalize(),
                area.City.Trim().Normalize(), area.State, area.Fee!.Value, true)
            { CoversAllNeighborhoods = area.CoversAllNeighborhoods }).ToArray()
            : JsonSerializer.Deserialize<DeliveryAreaResponse[]>(stored.AreasJson)!;
        var fixedFee = legacy.Value.FixedFee;
        areas = areas.Select(area => fixedFee is null ? area : area with { Fee = fixedFee.Value })
            .OrderBy(area => area.Id, StringComparer.Ordinal).ToArray();
        var content = $"{stored?.Version ?? 0}\n{Serialize(areas)}";
        if (fixedFee is not null)
            content += "\nFixedFee:" + fixedFee.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new DeliverySettingsResponse(areas, revision, stored?.UpdatedAt, stored?.UpdatedByName) { FixedFee = fixedFee };
    }

    private static string Serialize(DeliveryAreaResponse[] areas) => JsonSerializer.Serialize(areas);
}
