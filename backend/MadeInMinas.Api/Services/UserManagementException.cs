namespace MadeInMinas.Api.Services;

public enum UserManagementError
{
    UserNotFound,
    InvalidRole,
    DuplicateUsername,
    LastAdministrator,
    InvalidSession,
    PermissionDenied,
    InvalidPassword,
    UseOwnPasswordEndpoint
}

public sealed class UserManagementException(UserManagementError error, string message) : Exception(message)
{
    public UserManagementError Error { get; } = error;
}
