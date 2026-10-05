namespace MadeInMinas.Api.DTOs.PublicOrders;

public sealed record PublicOrderAccessResponse(string Token, DateTimeOffset ExpiresAt);

public sealed record PublicOrderTrackingResponse(int Number, string Fulfillment, decimal Total, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, PublicOrderStatusResponse[] History);

public sealed record PublicOrderStatusResponse(string Status, DateTimeOffset OccurredAt);
