using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MadeInMinas.Api.DTOs.Cart;

namespace MadeInMinas.Api.DTOs.PublicCart;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicCartQuoteRequest(
    [Required, MinLength(1), MaxLength(50)] CartItemRequest[] Items,
    [StringLength(500)] string? Notes = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Items is not null && Items.Any(item => item is null))
            yield return new ValidationResult("Os itens não podem ser nulos.", [nameof(Items)]);
        if (Items is not null && Items.Where(item => item is not null).GroupBy(item => item.ProductId)
            .Any(group => group.Sum(item => (long)(item.Quantity ?? 0)) > 99))
            yield return new ValidationResult("O limite é de 99 unidades por produto, somando todas as linhas.", [nameof(Items)]);
    }
}

public sealed record PublicCartQuoteResponse(
    CartItemResponse[] Items, string? Notes, decimal Subtotal, DateTimeOffset CalculatedAt);
