using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MadeInMinas.Api.DTOs.Cart;

public sealed class CartProductQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(120)] public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CartItemRequest(
    [Required] Guid? ProductId,
    [Required, Range(1, 99)] int? Quantity,
    [StringLength(250)] string? Notes = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProductId == Guid.Empty)
            yield return new ValidationResult("Selecione um produto.", [nameof(ProductId)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CartQuoteRequest(
    [Required] Guid? CustomerId,
    [Required, RegularExpression("^(Pickup|Delivery)$")] string Fulfillment,
    [Required, MinLength(1), MaxLength(50)] CartItemRequest[] Items,
    Guid? AddressId = null,
    [StringLength(500)] string? Notes = null) : IValidatableObject
{
    public decimal? DeliveryFee { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Fulfillment == "Delivery" && DeliveryFee is null)
            yield return new ValidationResult("Informe a taxa de entrega, inclusive quando for zero.", [nameof(DeliveryFee)]);
        if (DeliveryFee is < 0 or > 9999.99m || DeliveryFee is { } fee && decimal.Round(fee, 2) != fee)
            yield return new ValidationResult("A taxa deve ser de R$ 0,00 a R$ 9.999,99, com até duas casas decimais.", [nameof(DeliveryFee)]);
        if (Fulfillment == "Pickup" && DeliveryFee is not null and not 0)
            yield return new ValidationResult("Retirada não deve incluir taxa de entrega.", [nameof(DeliveryFee)]);
        if (CustomerId == Guid.Empty)
            yield return new ValidationResult("Selecione um cliente.", [nameof(CustomerId)]);
        if (Fulfillment == "Delivery" && (AddressId is null || AddressId == Guid.Empty))
            yield return new ValidationResult("Selecione o endereço para entrega.", [nameof(AddressId)]);
        if (Fulfillment == "Pickup" && AddressId is not null)
            yield return new ValidationResult("Retirada não deve incluir endereço de entrega.", [nameof(AddressId)]);
        if (Items is not null && Items.Any(item => item is null))
            yield return new ValidationResult("Os itens não podem ser nulos.", [nameof(Items)]);
        if (Items is not null && Items.Where(item => item is not null).GroupBy(item => item.ProductId)
            .Any(group => group.Sum(item => (long)(item.Quantity ?? 0)) > 99))
            yield return new ValidationResult("O limite é de 99 unidades por produto, somando todas as linhas.", [nameof(Items)]);
    }
}

public sealed record CartProductResponse(Guid Id, Guid CategoryId, string CategoryName,
    string Name, string? Description, decimal Price);
public sealed record CartProductPageResponse(CartProductResponse[] Items, int Page, int PageSize, int TotalCount);
public sealed record CartCustomerResponse(Guid Id, string Name, string Phone);
public sealed record CartAddressResponse(Guid Id, string Street, string Number, string Neighborhood,
    string City, string State, string? Complement, string? PostalCode, string? Reference);
public sealed record CartItemResponse(Guid ProductId, string Name, int Quantity, decimal UnitPrice,
    decimal LineTotal, string? Notes);
public sealed record CartQuoteResponse(CartCustomerResponse Customer, string Fulfillment, CartAddressResponse? Address,
    CartItemResponse[] Items, string? Notes, decimal Subtotal, DateTimeOffset CalculatedAt)
{
    public decimal DeliveryFee { get; init; }
    public decimal Total { get; init; }
    public string ReviewToken { get; init; } = "";
}
