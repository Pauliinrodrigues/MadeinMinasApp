namespace MadeInMinas.Api.Services;

public enum ProductError
{
    ProductNotFound, DuplicateProductName, InvalidProductCategory, InactiveProductCategory, InvalidSession, PermissionDenied,
    InvalidProductImage, ProductImageTooLarge, ProductImageNotFound
}

public sealed class ProductException(ProductError error, string message) : Exception(message)
{
    public ProductError Error { get; } = error;
}
