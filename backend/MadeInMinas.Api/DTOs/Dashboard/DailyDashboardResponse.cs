namespace MadeInMinas.Api.DTOs.Dashboard;

public sealed record DailyDashboardResponse(
    DateOnly Date,
    string TimeZone,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset CalculatedAt,
    DailyOrdersResponse Orders,
    DailyReceiptsResponse Receipts,
    DashboardQueueResponse[] Queues,
    DailyProductionResponse Production,
    DashboardProductResponse[] TopProducts);

public sealed record DailyOrdersResponse(int Created, int CreatedAndCancelled, int Confirmed, decimal ConfirmedValue, decimal? AverageTicket);
public sealed record DailyReceiptsResponse(decimal Received, decimal Refunded, decimal NetReceived);
public sealed record DashboardQueueResponse(string Status, int Count);
public sealed record DailyProductionResponse(int Completed, decimal? AverageMinutes);
public sealed record DashboardProductResponse(Guid ProductId, string ProductName, long Quantity, decimal ItemValue);
