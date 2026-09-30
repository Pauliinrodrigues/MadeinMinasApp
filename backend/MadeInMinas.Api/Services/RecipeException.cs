namespace MadeInMinas.Api.Services;

public enum RecipeError
{
    ProductNotFound, RecipeNotFound, InvalidRecipeIngredient, InactiveRecipeIngredient, InvalidSession, PermissionDenied
}

public sealed class RecipeException(RecipeError error, string message) : Exception(message)
{
    public RecipeError Error { get; } = error;
}
