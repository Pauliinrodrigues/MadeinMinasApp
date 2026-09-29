namespace MadeInMinas.Api.Services;

public enum ProductError { ProductNotFound, DuplicateProductName, InvalidProductCategory, InactiveProductCategory, InvalidSession, PermissionDenied }

public sealed class ProductException(ProductError error, string message) : Exception(message)
{
    public ProductError Error { get; } = error;
}

