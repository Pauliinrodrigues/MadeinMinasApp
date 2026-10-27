using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.PublicCart;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.Validation;

namespace MadeInMinas.Api.DTOs.ChatOrders;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatOrderDetailsRequest(
    [StringLength(120)] string? Name = null,
    [StringLength(30)] string? Phone = null,
    [RegularExpression("^(Pickup|Delivery)$")] string? Fulfillment = null,
    PublicDeliveryAddressRequest? Address = null,
    [StringLength(500)] string? Notes = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && string.IsNullOrWhiteSpace(Name))
            yield return new ValidationResult("Informe o nome para o pedido.", [nameof(Name)]);
        if (Phone is not null && !BrazilianPhone.TryNormalize(Phone, out _))
            yield return new ValidationResult("Informe telefone brasileiro com DDD.", [nameof(Phone)]);
        if (Fulfillment == "Pickup" && Address is not null)
            yield return new ValidationResult("Retirada não deve incluir endereço.", [nameof(Address)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatOrderReviewRequest([Required, Range(1, int.MaxValue)] int? ExpectedDraftVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatOrderConfirmationRequest(
    [Required] Guid? RequestId,
    [Required, Range(1, int.MaxValue)] int? ExpectedDraftVersion,
    [Required, RegularExpression("^[A-F0-9]{64}$")] string ReviewToken) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequestId == Guid.Empty)
            yield return new ValidationResult("Informe o identificador da confirmação.", [nameof(RequestId)]);
    }
}

public sealed record ChatOrderStateResponse(int DraftVersion, CartItemResponse[] Items,
    string Name, string? Phone, string? Fulfillment, PublicDeliveryAddressRequest? Address, string? Notes,
    PublicCartQuoteResponse? Quote, PublicCheckoutReviewResponse? Review, PublicOrderReceipt? Receipt,
    string[] MissingFields, bool NeedsReview);
