namespace MadeInMinas.Api.Services;

public enum CustomerError
{
    CustomerNotFound, AddressNotFound, DuplicateCustomerPhone, InvalidCustomerPhone, InvalidSession, PermissionDenied
}

public sealed class CustomerException(CustomerError error, string message) : Exception(message)
{
    public CustomerError Error { get; } = error;
}
