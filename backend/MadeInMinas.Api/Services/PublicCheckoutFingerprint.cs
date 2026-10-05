using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.Validation;

namespace MadeInMinas.Api.Services;

internal static class PublicCheckoutFingerprint
{
    public static string ForReview(PublicCheckoutReviewResponse review) => Hash(new
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

    public static string ForRequest(PublicOrderRequest request)
    {
        BrazilianPhone.TryNormalize(request.Checkout.Phone, out var phone);
        return Hash(new
        {
            request.ReviewToken,
            Name = Clean(request.Checkout.Name),
            Phone = phone,
            Notes = Clean(request.Checkout.Cart.Notes),
            Items = request.Checkout.Cart.Items.Select(item => new { item.ProductId, item.Quantity, Notes = Clean(item.Notes) })
        });
    }

    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Normalize();
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
