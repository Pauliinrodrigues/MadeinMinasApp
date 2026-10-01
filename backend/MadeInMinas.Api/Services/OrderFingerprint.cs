using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.Orders;

namespace MadeInMinas.Api.Services;

// Detecta mudanças no conteúdo revisado; autorização e preços continuam sendo verificados no servidor.
public static class OrderFingerprint
{
    public static string ForReview(CartQuoteResponse quote) => Hash(new
    {
        quote.Customer,
        quote.Fulfillment,
        quote.Address,
        Items = quote.Items.Select(item => new
        {
            item.ProductId,
            item.Name,
            item.Quantity,
            UnitPrice = Money(item.UnitPrice),
            LineTotal = Money(item.LineTotal),
            item.Notes
        }),
        quote.Notes,
        Subtotal = Money(quote.Subtotal),
        DeliveryFee = Money(quote.DeliveryFee),
        Total = Money(quote.Total)
    });

    public static string ForRequest(CreateOrderRequest request) => Hash(new
    {
        request.ReviewToken,
        request.Cart.CustomerId,
        request.Cart.Fulfillment,
        request.Cart.AddressId,
        Notes = Clean(request.Cart.Notes),
        DeliveryFee = Money(request.Cart.DeliveryFee ?? 0),
        Items = request.Cart.Items.Select(item => new { item.ProductId, item.Quantity, Notes = Clean(item.Notes) })
    });

    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Normalize();
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
