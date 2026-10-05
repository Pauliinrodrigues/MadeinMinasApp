using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.PublicCart;
using MadeInMinas.Api.Validation;

namespace MadeInMinas.Api.DTOs.PublicCheckout;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicCheckoutRequest(
    [Required, StringLength(120)] string Name,
    [Required, StringLength(30)] string Phone,
    [Required] PublicCartQuoteRequest Cart) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!BrazilianPhone.TryNormalize(Phone, out _))
            yield return new ValidationResult("Informe telefone brasileiro com DDD.", [nameof(Phone)]);
    }
}

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
    string ReviewToken, DateTimeOffset CalculatedAt);

// Comprovante de recebimento, sem identificadores internos ou dados pessoais do cadastro.
public sealed record PublicOrderReceipt(int Number, string Fulfillment, decimal Total, DateTimeOffset CreatedAt);
public sealed record PublicOrderCreationResult(PublicOrderReceipt Receipt, bool Created);
