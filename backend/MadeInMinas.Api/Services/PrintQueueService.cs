using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed record PrintSettingsResponse(bool Registered, bool Automatic, int Version, DateTimeOffset? LastSeenAt);
public sealed record PrintJobResponse(Guid Id, Guid OrderId, long OrderNumber, string Mode, string State,
    DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);
public sealed record PrintClaimResponse(Guid Id, Guid ClaimId, string[] Lines);
public sealed class PrintingException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class PrintQueueService(AppDbContext database, TimeProvider clock)
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private static PrintSettingsResponse Settings(PrintStation station) =>
        new(station.KeyHash is not null, station.Automatic, station.Version, station.LastSeenAt);
    public static PrintJobResponse Summary(PrintJob job) =>
        new(job.Id, job.OrderId, job.OrderNumber, job.Mode, job.State, job.CreatedAt, job.FinishedAt);

    public async Task<PrintSettingsResponse> GetSettingsAsync(CancellationToken ct) =>
        Settings(await database.PrintStations.AsNoTracking().SingleAsync(ct));

    public async Task<(PrintSettingsResponse Settings, string Key)> RegisterAsync(int expectedVersion, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var station = await LockStationAsync(ct);
        CheckVersion(station, expectedVersion);
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        station.KeyHash = Hash(key);
        station.Automatic = false;
        station.LastSeenAt = null;
        station.Version++;
        // Revogar a estação não permite reenviar trabalhos cujo resultado é desconhecido.
        await database.PrintJobs.Where(job => job.State == "Claimed")
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.State, "Review"), ct);
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (Settings(station), key);
    }

    public async Task<PrintSettingsResponse> SetAutomaticAsync(bool automatic, int expectedVersion, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var station = await LockStationAsync(ct);
        CheckVersion(station, expectedVersion);
        if (automatic && (station.KeyHash is null || station.LastSeenAt < clock.GetUtcNow().AddSeconds(-30) || station.LastSeenAt is null))
            throw new PrintingException(409, "Inicie o agente Windows antes de ativar a impressão automática.");
        station.Automatic = automatic;
        station.Version++;
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Settings(station);
    }

    // Chamado dentro da transação da mudança de status. Ativar a estação não percorre pedidos antigos.
    public async Task AutomaticAsync(Guid orderId, string mode, int version, CancellationToken ct)
    {
        if (!await database.PrintStations.AnyAsync(station => station.Automatic && station.KeyHash != null, ct))
            return;
        await AddAsync(orderId, mode, $"auto:{orderId}:{version}:{mode}", false, ct);
    }

    public async Task CancelQueuedAsync(Guid orderId, CancellationToken ct) =>
        await database.PrintJobs.Where(job => job.OrderId == orderId && job.State == "Queued")
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.State, "Cancelled")
                .SetProperty(job => job.FinishedAt, clock.GetUtcNow()), ct);

    public async Task<PrintJobResponse> ManualAsync(Guid orderId, string mode, Guid requestId, int expectedVersion, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var order = await database.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new PrintingException(404, "Pedido não encontrado.");
        var station = await LockStationAsync(ct);
        var key = $"manual:{requestId}";
        var existing = await database.PrintJobs.SingleOrDefaultAsync(job => job.RequestKey == key, ct);
        if (existing is not null)
        {
            if (existing.OrderId != orderId || existing.Mode != mode)
                throw new PrintingException(409, "Solicitação de impressão já utilizada.");
            await transaction.CommitAsync(ct);
            return Summary(existing);
        }
        if (order.Version != expectedVersion)
            throw new PrintingException(409, "O pedido mudou. Atualize a comanda antes de enviar.");
        if (station.KeyHash is null)
            throw new PrintingException(409, "Cadastre a estação em Impressão automática antes de enviar.");
        var job = await AddAsync(orderId, mode, key, true, ct);
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Summary(job);
    }

    public async Task<PrintJobResponse[]> ListAsync(CancellationToken ct)
    {
        await ExpireAsync(ct);
        var jobs = await database.PrintJobs.AsNoTracking().OrderByDescending(job => job.CreatedAt).Take(100).ToArrayAsync(ct);
        return jobs.Select(Summary).ToArray();
    }

    public async Task CancelJobAsync(Guid id, CancellationToken ct)
    {
        var count = await database.PrintJobs.Where(job => job.Id == id && job.State == "Queued")
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.State, "Cancelled")
                .SetProperty(job => job.FinishedAt, clock.GetUtcNow()), ct);
        if (count == 0)
            throw new PrintingException(409, "Somente envios aguardando impressão podem ser cancelados.");
    }

    public async Task<PrintClaimResponse?> ClaimAsync(string key, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var station = await LockStationAsync(ct);
        VerifyKey(station, key);
        station.LastSeenAt = clock.GetUtcNow();
        await ExpireAsync(ct);
        // Uma estação pode ter vários processos, mas somente um trabalho em andamento.
        var job = await database.PrintJobs.FromSqlRaw(
            "SELECT * FROM \"PrintJobs\" WHERE \"State\" = 'Queued' AND NOT EXISTS (SELECT 1 FROM \"PrintJobs\" WHERE \"State\" = 'Claimed') ORDER BY \"CreatedAt\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED")
            .SingleOrDefaultAsync(ct);
        if (job is not null)
        {
            job.State = "Claimed";
            job.ClaimId = Guid.NewGuid();
            job.ClaimedAt = clock.GetUtcNow();
        }
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return job is null ? null : new(job.Id, job.ClaimId!.Value, JsonSerializer.Deserialize<string[]>(job.Payload)!);
    }

    public async Task FinishAsync(string key, Guid id, Guid claimId, bool submitted, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var station = await LockStationAsync(ct);
        VerifyKey(station, key);
        var job = await database.PrintJobs.FromSqlInterpolated($"SELECT * FROM \"PrintJobs\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new PrintingException(404, "Envio não encontrado.");
        if (job.ClaimId != claimId || job.State is not ("Claimed" or "Review" or "Submitted"))
            throw new PrintingException(409, "Envio não pertence a esta solicitação.");
        if (job.State != "Submitted")
        {
            job.State = submitted ? "Submitted" : "Review";
            job.FinishedAt = clock.GetUtcNow();
        }
        station.LastSeenAt = clock.GetUtcNow();
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task ExpireAsync(CancellationToken ct) =>
        await database.PrintJobs.Where(job => job.State == "Claimed" && job.ClaimedAt < clock.GetUtcNow().AddMinutes(-2))
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.State, "Review"), ct);

    private async Task<PrintJob> AddAsync(Guid orderId, string mode, string requestKey, bool manual, CancellationToken ct)
    {
        var order = await database.Orders.AsNoTracking().Include(order => order.Items).SingleAsync(order => order.Id == orderId, ct);
        var lines = new List<string> { "MADE IN MINAS", mode == "kitchen" ? "VIA DE PRODUÇÃO" : "VIA DE EXPEDIÇÃO",
            $"Pedido #{order.Number}", order.Fulfillment == "Pickup" ? "RETIRADA" : "ENTREGA" };
        var status = order.Status switch
        {
            "New" => "Novo",
            "Confirmed" => "Confirmado",
            "InPreparation" => "Em preparação",
            "Ready" => "Pronto",
            "AwaitingDelivery" => "Aguardando entrega",
            "OutForDelivery" => "Saiu para entrega",
            "Delivered" => "Entregue",
            "Finalized" => "Finalizado",
            "Cancelled" => "Cancelado",
            _ => order.Status
        };
        lines.Add($"Situação: {status} — versão {order.Version}");
        if (manual)
            lines.Add("CÓPIA MANUAL");
        if (order.Status == "New")
            lines.Add("AGUARDANDO CONFIRMAÇÃO — NÃO PRODUZIR");
        if (order.Status == "Cancelled")
            lines.Add("CANCELADO — NÃO PRODUZIR / NÃO ENTREGAR");
        if (order.Status == "Finalized")
            lines.Add("PEDIDO FINALIZADO");
        lines.Add("Criado: " + TimeZoneInfo.ConvertTime(order.CreatedAt, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).ToString("dd/MM/yyyy HH:mm", Portuguese));
        lines.Add("--------------------------------");
        foreach (var item in order.Items.OrderBy(item => item.Position))
        {
            lines.Add($"{item.Quantity} x {item.ProductName}");
            if (!string.IsNullOrWhiteSpace(item.Notes))
                lines.Add("Obs.: " + item.Notes);
            if (mode == "dispatch")
                lines.Add(item.LineTotal.ToString("C", Portuguese));
        }
        if (!string.IsNullOrWhiteSpace(order.Notes))
            lines.Add("Observações: " + order.Notes);
        if (mode == "dispatch")
        {
            lines.Add("--------------------------------");
            lines.Add("Cliente: " + order.CustomerName);
            lines.Add("Telefone: " + order.CustomerPhone);
            if (order.Fulfillment == "Delivery")
            {
                lines.Add($"{order.AddressStreet}, {order.AddressNumber}");
                lines.Add($"{order.AddressNeighborhood} — {order.AddressCity}/{order.AddressState}");
                if (!string.IsNullOrWhiteSpace(order.AddressComplement))
                    lines.Add("Complemento: " + order.AddressComplement);
                if (!string.IsNullOrWhiteSpace(order.AddressReference))
                    lines.Add("Referência: " + order.AddressReference);
            }
            lines.Add("Produtos: " + order.Subtotal.ToString("C", Portuguese));
            lines.Add("Entrega: " + order.DeliveryFee.ToString("C", Portuguese));
            lines.Add("TOTAL: " + order.Total.ToString("C", Portuguese));
            var payment = await database.Payments.AsNoTracking().SingleOrDefaultAsync(payment => payment.OrderId == orderId
                && (payment.Status == "Pending" || payment.Status == "Received"), ct);
            if (payment is null)
                lines.Add("PAGAMENTO NÃO REGISTRADO — CONFERIR");
            else
            {
                var method = payment.Method switch { "Cash" => "Dinheiro", "Pix" => "Pix", "CreditCard" => "Crédito", "DebitCard" => "Débito", _ => payment.Method };
                lines.Add($"Pagamento: {method} — {(payment.Status == "Received" ? "Recebido" : "Pendente")}");
                if (payment.CashTendered is { } cash)
                {
                    lines.Add("Dinheiro informado: " + cash.ToString("C", Portuguese));
                    lines.Add("Troco: " + (cash - payment.Amount).ToString("C", Portuguese));
                }
            }
        }
        lines.Add("--------------------------------");
        lines.Add("COMANDA — NÃO É DOCUMENTO FISCAL");
        var job = new PrintJob
        {
            OrderId = orderId,
            OrderNumber = order.Number,
            Mode = mode,
            RequestKey = requestKey,
            Payload = JsonSerializer.Serialize(lines),
            CreatedAt = clock.GetUtcNow()
        };
        database.PrintJobs.Add(job);
        return job;
    }

    private Task<PrintStation> LockStationAsync(CancellationToken ct) =>
        database.PrintStations.FromSqlRaw("SELECT * FROM \"PrintStations\" WHERE \"Id\" = 1 FOR UPDATE").SingleAsync(ct);
    private static void CheckVersion(PrintStation station, int expectedVersion)
    {
        if (station.Version != expectedVersion)
            throw new PrintingException(409, "A configuração mudou. Atualize a página.");
    }
    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    private static void VerifyKey(PrintStation station, string key)
    {
        if (key.Length != 64 || station.KeyHash is null || !CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(station.KeyHash), Encoding.ASCII.GetBytes(Hash(key))))
            throw new PrintingException(401, "Credencial da estação inválida.");
    }
}
