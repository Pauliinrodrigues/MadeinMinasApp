using System.Data;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Reports;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class SalesReportService(AppDbContext database, TimeProvider clock)
{
    private const string TimeZoneId = "America/Sao_Paulo";
    private static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
    private static readonly string[] PaymentMethods = ["Cash", "Pix", "CreditCard", "DebitCard"];

    public async Task<SalesReportResponse> GetAsync(SalesReportQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, BusinessTimeZone).DateTime);
        var firstDate = query.StartDate ?? today.AddDays(-6);
        var lastDate = query.EndDate ?? today;
        var start = StartOfDay(firstDate);
        var end = StartOfDay(lastDate.AddDays(1));

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var orders = database.Orders.AsNoTracking();
        var created = await orders.Where(order => order.CreatedAt >= start && order.CreatedAt < end)
            .GroupBy(order => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(order.CreatedAt.UtcDateTime, TimeZoneId).Date)
            .Select(group => new { Date = group.Key, Count = group.Count(), Cancelled = group.Count(order => order.Status == "Cancelled") })
            .ToDictionaryAsync(item => DateOnly.FromDateTime(item.Date), cancellationToken);

        // Uma confirmação por pedido; cancelamentos posteriores são refletidos na consulta atual.
        var confirmations = orders.Where(order => order.Status != "Cancelled").Select(order => new
        {
            Order = order,
            At = order.History.Where(history => history.ToStatus == "Confirmed")
                .Select(history => (DateTimeOffset?)history.OccurredAt).Min()
        }).Where(item => item.At >= start && item.At < end);
        var confirmed = await confirmations
            .GroupBy(item => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(item.At!.Value.UtcDateTime, TimeZoneId).Date)
            .Select(group => new { Date = group.Key, Count = group.Count(), Value = group.Sum(item => item.Order.Total) })
            .ToDictionaryAsync(item => DateOnly.FromDateTime(item.Date), cancellationToken);

        var paymentEvents = from history in database.PaymentStatusHistory.AsNoTracking()
                            join payment in database.Payments.AsNoTracking() on history.PaymentId equals payment.Id
                            where history.OccurredAt >= start && history.OccurredAt < end
                                && (history.ToStatus == "Received" || history.ToStatus == "Refunded")
                            select new { history.OccurredAt, history.ToStatus, payment.Method, payment.Amount };
        var receipts = await paymentEvents.GroupBy(item => new
        {
            Date = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(item.OccurredAt.UtcDateTime, TimeZoneId).Date,
            item.Method
        }).Select(group => new
        {
            group.Key.Date,
            group.Key.Method,
            Received = group.Sum(item => item.ToStatus == "Received" ? item.Amount : 0m),
            Refunded = group.Sum(item => item.ToStatus == "Refunded" ? item.Amount : 0m)
        }).ToArrayAsync(cancellationToken);
        var receiptsByDay = receipts.GroupBy(item => DateOnly.FromDateTime(item.Date))
            .ToDictionary(group => group.Key, group => new { Received = group.Sum(item => item.Received), Refunded = group.Sum(item => item.Refunded) });

        var topProducts = await confirmations.SelectMany(item => item.Order.Items, (confirmation, item) => new
        {
            item.ProductId,
            item.ProductName,
            item.Quantity,
            item.LineTotal,
            item.Position,
            confirmation.Order.CreatedAt,
            confirmation.Order.Number
        }).GroupBy(item => item.ProductId).Select(group => new
        {
            ProductId = group.Key,
            ProductName = group.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Number)
                .ThenBy(item => item.Position).Select(item => item.ProductName).First(),
            Quantity = group.Sum(item => (long)item.Quantity),
            ItemValue = group.Sum(item => item.LineTotal)
        }).OrderByDescending(item => item.Quantity).ThenByDescending(item => item.ItemValue).ThenBy(item => item.ProductId)
            .Take(10).ToArrayAsync(cancellationToken);

        var days = Enumerable.Range(0, lastDate.DayNumber - firstDate.DayNumber + 1).Select(offset =>
        {
            var date = firstDate.AddDays(offset);
            var dayCreated = created.GetValueOrDefault(date);
            var dayConfirmed = confirmed.GetValueOrDefault(date);
            var dayReceipts = receiptsByDay.GetValueOrDefault(date);
            return new SalesReportDayResponse(date, Metrics(dayCreated?.Count ?? 0, dayCreated?.Cancelled ?? 0,
                dayConfirmed?.Count ?? 0, dayConfirmed?.Value ?? 0, dayReceipts?.Received ?? 0, dayReceipts?.Refunded ?? 0));
        }).ToArray();
        // A média do período é ponderada pelos pedidos, não pela quantidade de dias.
        var summary = Metrics(days.Sum(day => day.Metrics.Created), days.Sum(day => day.Metrics.CreatedAndCancelled),
            days.Sum(day => day.Metrics.Confirmed), days.Sum(day => day.Metrics.ConfirmedValue),
            days.Sum(day => day.Metrics.Received), days.Sum(day => day.Metrics.Refunded));
        var methods = PaymentMethods.Select(method =>
        {
            var received = receipts.Where(item => item.Method == method).Sum(item => item.Received);
            var refunded = receipts.Where(item => item.Method == method).Sum(item => item.Refunded);
            return new PaymentMethodReceiptsResponse(method, received, refunded, received - refunded);
        }).ToArray();
        await transaction.CommitAsync(cancellationToken);
        return new SalesReportResponse(firstDate, lastDate, TimeZoneId, start, end, now, summary, days, methods,
            topProducts.Select(item => new SalesReportProductResponse(item.ProductId, item.ProductName, item.Quantity, item.ItemValue)).ToArray());
    }

    private static SalesMetricsResponse Metrics(int created, int cancelled, int confirmed, decimal value, decimal received, decimal refunded) =>
        new(created, cancelled, confirmed, value,
            confirmed == 0 ? null : decimal.Round(value / confirmed, 2, MidpointRounding.AwayFromZero), received, refunded, received - refunded);

    private static DateTimeOffset StartOfDay(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        // Datas históricas de início do horário de verão podem não ter meia-noite local.
        while (BusinessTimeZone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        if (BusinessTimeZone.IsAmbiguousTime(local))
            return new DateTimeOffset(local, BusinessTimeZone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, BusinessTimeZone));
    }
}
