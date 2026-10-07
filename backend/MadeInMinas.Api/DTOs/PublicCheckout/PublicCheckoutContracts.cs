using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.PublicCart;
using MadeInMinas.Api.DTOs.PublicOrders;
using MadeInMinas.Api.Validation;

namespace MadeInMinas.Api.DTOs.PublicCheckout;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicCheckoutRequest(
    [Required, StringLength(120)] string Name,
    [Required, StringLength(30)] string Phone,
    [Required] PublicCartQuoteRequest Cart) : IValidatableObject
{
    [Required, RegularExpression("^(Pickup|Delivery)$")]
    public string Fulfillment { get; init; } = "Pickup";
    public PublicDeliveryAddressRequest? Address { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!BrazilianPhone.TryNormalize(Phone, out _))
            yield return new ValidationResult("Informe telefone brasileiro com DDD.", [nameof(Phone)]);
        if (Fulfillment == "Delivery" && Address is null)
            yield return new ValidationResult("Informe o endereço para entrega.", [nameof(Address)]);
        if (Fulfillment == "Pickup" && Address is not null)
            yield return new ValidationResult("Retirada não deve incluir endereço.", [nameof(Address)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicDeliveryAddressRequest(
    [Required, RegularExpression("^[a-z0-9][a-z0-9-]{0,59}$")] string AreaId,
    [Required, StringLength(120)] string Street,
    [Required, StringLength(20)] string Number,
    [StringLength(120)] string? Complement = null,
    [StringLength(10)] string? PostalCode = null,
    [StringLength(250)] string? Reference = null) : IValidatableObject
{
    [StringLength(80)]
    public string? Neighborhood { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Neighborhood is not null && string.IsNullOrWhiteSpace(Neighborhood))
            yield return new ValidationResult("Informe o bairro para entrega.", [nameof(Neighborhood)]);
        if (!string.IsNullOrWhiteSpace(PostalCode) && !Regex.IsMatch(PostalCode.Trim(), "^[0-9]{5}-?[0-9]{3}$"))
            yield return new ValidationResult("Informe CEP com oito dígitos, com ou sem hífen.", [nameof(PostalCode)]);
    }
}

public sealed record PublicDeliveryAreaResponse(string Id, string Neighborhood, string City, string State, decimal Fee)
{
    public bool CoversAllNeighborhoods { get; init; }
}
public sealed record PublicCheckoutOptionsResponse(decimal? FixedDeliveryFee, decimal PickupFee, string? PickupAddress);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicOrderRequest(
    [Required] Guid? RequestId,
    [Required, RegularExpression("^[A-F0-9]{64}$")] string ReviewToken,
    [Required] PublicCheckoutRequest Checkout) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequestId == Guid.Empty)
            yield return new ValidationResult("Informe o identificador da tentativa de envio.", [nameof(RequestId)]);
    }
}

public sealed record PublicCheckoutReviewResponse(string Name, string Phone, string Fulfillment,
    CartItemResponse[] Items, string? Notes, decimal Subtotal, decimal DeliveryFee, decimal Total,
    string ReviewToken, DateTimeOffset CalculatedAt)
{
    public CartAddressResponse? Address { get; init; }
    public string? DeliveryAreaId { get; init; }
}

// Comprovante de recebimento, sem identificadores internos ou dados pessoais do cadastro.
public sealed record PublicOrderReceipt(int Number, string Fulfillment, decimal Total, DateTimeOffset CreatedAt)
{
    public PublicOrderAccessResponse? Tracking { get; init; }
}
public sealed record PublicOrderCreationResult(PublicOrderReceipt Receipt, bool Created);
