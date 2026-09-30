namespace MadeInMinas.Api.Services;

public enum CategoryError
{
    CategoryNotFound, DuplicateCategoryName, InvalidSession, PermissionDenied
}

public sealed class CategoryException(CategoryError error, string message) : Exception(message)
{
    public CategoryError Error { get; } = error;
}

