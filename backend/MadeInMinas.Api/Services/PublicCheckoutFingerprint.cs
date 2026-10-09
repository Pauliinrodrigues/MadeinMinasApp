using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.Validation;

namespace MadeInMinas.Api.Services;

internal static class PublicCheckoutFingerprint
{
    public static string ForReview(PublicCheckoutReviewResponse review)
    {
        // Preservar o hash original de retirada permite recuperar envios anteriores à 8F.
        var original = Hash(new
        {
            review.Name,
            review.Phone,
            review.Fulfillment,
            Items = review.Items.Select(item => new
            {
                item.ProductId,
                item.Name,
                item.Quantity,
                item.Notes,
                UnitPrice = Money(item.UnitPrice),
                LineTotal = Money(item.LineTotal)
            }),
            review.Notes,
            Subtotal = Money(review.Subtotal),
            DeliveryFee = Money(review.DeliveryFee),
            Total = Money(review.Total)
        });
        return review.Fulfillment == "Pickup" ? original
            : Hash(new { Original = original, review.DeliveryAreaId, review.Address });
    }

    public static string ForRequest(PublicOrderRequest request)
    {
        BrazilianPhone.TryNormalize(request.Checkout.Phone, out var phone);
        var original = Hash(new
        {
            request.ReviewToken,
            Name = Clean(request.Checkout.Name),
            Phone = phone,
            Notes = Clean(request.Checkout.Cart.Notes),
            Items = request.Checkout.Cart.Items.Select(item => new { item.ProductId, item.Quantity, Notes = Clean(item.Notes) })
        });
        if (request.Checkout.Fulfillment == "Pickup")
            return original;
        var address = request.Checkout.Address!;
        var delivery = Hash(new
        {
            Original = original,
            request.Checkout.Fulfillment,
            address.AreaId,
            Street = Clean(address.Street),
            Number = Clean(address.Number),
            Complement = Clean(address.Complement),
            PostalCode = Clean(address.PostalCode)?.Replace("-", "", StringComparison.Ordinal),
            Reference = Clean(address.Reference)
        });
        // Endereços anteriores, sem bairro livre, conservam o hash de recuperação.
        return address.Neighborhood is null ? delivery
            : Hash(new { Original = delivery, Neighborhood = Clean(address.Neighborhood) });
    }

    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Normalize();
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
