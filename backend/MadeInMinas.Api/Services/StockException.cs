namespace MadeInMinas.Api.Services;

public enum StockError
{
    IngredientNotFound, InactiveIngredient, StockVersionConflict, StockRequestConflict,
    InsufficientStock, StockLimitExceeded, StockUnchanged, InvalidSession, PermissionDenied
}

public sealed class StockException(StockError error, string message) : Exception(message)
{
    public StockError Error { get; } = error;
}
