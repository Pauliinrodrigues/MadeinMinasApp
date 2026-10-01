using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MadeInMinas.Api.DTOs.Kitchen;

public sealed class KitchenBoardQuery
{
    [Range(1, 1_000_000)] public int ConfirmedPage { get; init; } = 1;
    [Range(1, 1_000_000)] public int PreparingPage { get; init; } = 1;
    [Range(1, 1_000_000)] public int ReadyPage { get; init; } = 1;
    [Range(1, 50)] public int PageSize { get; init; } = 20;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record KitchenStatusRequest(
    [Required, RegularExpression("^(InPreparation|Ready)$")] string Status,
    [Required, Range(1, int.MaxValue)] int? ExpectedVersion);

public sealed record KitchenItemResponse(int Position, string Name, int Quantity, string? Notes);
public sealed record KitchenOrderResponse(Guid Id, int Number, string Fulfillment, string Status, int Version,
    string? Notes, DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt, DateTimeOffset? PreparationStartedAt,
    DateTimeOffset? ReadyAt, KitchenItemResponse[] Items);
public sealed record KitchenColumnResponse(string Status, KitchenOrderResponse[] Items, int Page, int PageSize, int TotalCount);
public sealed record KitchenBoardResponse(DateTimeOffset ServerTime, KitchenColumnResponse[] Columns);
