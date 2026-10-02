using System.Data;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class DashboardService(AppDbContext database, TimeProvider clock)
{
    private static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly string[] QueueStatuses = ["New", "Confirmed", "InPreparation", "Ready", "AwaitingDelivery", "OutForDelivery", "Delivered"];

    public async Task<DailyDashboardResponse> TodayAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, BusinessTimeZone).DateTime);
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), BusinessTimeZone));
        var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessTimeZone));

        // Todos os indicadores pertencem à mesma visão dos pedidos e históricos.
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var orders = database.Orders.AsNoTracking();
        var created = await orders.Where(order => order.CreatedAt >= start && order.CreatedAt < end)
            .GroupBy(_ => 1).Select(group => new { Count = group.Count(), Cancelled = group.Count(order => order.Status == "Cancelled") })
            .SingleOrDefaultAsync(cancellationToken);
        var confirmedOrders = orders.Where(order => order.Status != "Cancelled" && order.History.Any(history =>
            history.ToStatus == "Confirmed" && history.OccurredAt >= start && history.OccurredAt < end));
        var confirmed = await confirmedOrders.GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Value = group.Sum(order => order.Total) })
            .SingleOrDefaultAsync(cancellationToken);
        var confirmedValue = confirmed?.Value ?? 0m;
        var confirmedCount = confirmed?.Count ?? 0;

        // O status atual não substitui a data dos eventos: um estorno não apaga o recebimento.
        var paymentEvents = from history in database.PaymentStatusHistory.AsNoTracking()
                            join payment in database.Payments.AsNoTracking() on history.PaymentId equals payment.Id
                            where history.OccurredAt >= start && history.OccurredAt < end
                                && (history.ToStatus == "Received" || history.ToStatus == "Refunded")
                            select new { history.ToStatus, payment.Amount };
        var receipts = await paymentEvents.GroupBy(_ => 1).Select(group => new
        {
            Received = group.Sum(item => item.ToStatus == "Received" ? item.Amount : 0m),
            Refunded = group.Sum(item => item.ToStatus == "Refunded" ? item.Amount : 0m)
        }).SingleOrDefaultAsync(cancellationToken);
        var received = receipts?.Received ?? 0m;
        var refunded = receipts?.Refunded ?? 0m;

        var queueCounts = await orders.Where(order => QueueStatuses.Contains(order.Status))
            .GroupBy(order => order.Status).Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);

        var productionTimes = await database.OrderStatusHistory.AsNoTracking()
            .Where(history => history.ToStatus == "Ready" && history.OccurredAt >= start && history.OccurredAt < end)
            .Select(ready => new
            {
                ReadyAt = ready.OccurredAt,
                StartedAt = database.OrderStatusHistory.Where(started => started.OrderId == ready.OrderId
                    && started.ToStatus == "InPreparation" && started.OccurredAt <= ready.OccurredAt)
                    .Select(started => (DateTimeOffset?)started.OccurredAt).Min()
            }).ToArrayAsync(cancellationToken);
        var durations = productionTimes.Where(item => item.StartedAt.HasValue)
            .Select(item => (decimal)(item.ReadyAt - item.StartedAt!.Value).Ticks / TimeSpan.TicksPerMinute).ToArray();

        var soldItems = confirmedOrders.SelectMany(order => order.Items, (order, item) => new
        {
            item.ProductId,
            item.ProductName,
            item.Quantity,
            item.LineTotal,
            item.Position,
            order.CreatedAt,
            order.Number
        });
        var topProducts = await soldItems.GroupBy(item => item.ProductId).Select(group => new
        {
            ProductId = group.Key,
            ProductName = group.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Number)
                .ThenBy(item => item.Position).Select(item => item.ProductName).First(),
            Quantity = group.Sum(item => (long)item.Quantity),
            ItemValue = group.Sum(item => item.LineTotal)
        }).OrderByDescending(item => item.Quantity).ThenByDescending(item => item.ItemValue).ThenBy(item => item.ProductId)
            .Take(5).ToArrayAsync(cancellationToken);

        var response = new DailyDashboardResponse(date, BusinessTimeZone.Id, start, end, now,
            new DailyOrdersResponse(created?.Count ?? 0, created?.Cancelled ?? 0, confirmedCount, confirmedValue,
                confirmedCount == 0 ? null : decimal.Round(confirmedValue / confirmedCount, 2, MidpointRounding.AwayFromZero)),
            new DailyReceiptsResponse(received, refunded, received - refunded),
            QueueStatuses.Select(status => new DashboardQueueResponse(status, queueCounts.GetValueOrDefault(status))).ToArray(),
            new DailyProductionResponse(durations.Length,
                durations.Length == 0 ? null : decimal.Round(durations.Average(), 2, MidpointRounding.AwayFromZero)),
            topProducts.Select(item => new DashboardProductResponse(item.ProductId, item.ProductName, item.Quantity, item.ItemValue)).ToArray());
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}
