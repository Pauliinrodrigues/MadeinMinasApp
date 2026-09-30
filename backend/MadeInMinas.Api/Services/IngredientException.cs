namespace MadeInMinas.Api.Services;

public enum IngredientError
{
    IngredientNotFound, DuplicateIngredientName, IngredientUnitImmutable, InvalidSession, PermissionDenied
}

public sealed class IngredientException(IngredientError error, string message) : Exception(message)
{
    public IngredientError Error { get; } = error;
}
