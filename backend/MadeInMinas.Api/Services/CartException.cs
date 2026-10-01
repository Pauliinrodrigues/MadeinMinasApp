namespace MadeInMinas.Api.Services;

public enum CartError
{
    CartCustomerUnavailable,
    CartAddressUnavailable,
    CartProductUnavailable
}

public sealed class CartException(CartError error, string message) : Exception(message)
{
    public CartError Error { get; } = error;
}
