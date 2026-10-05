namespace MadeInMinas.Api.Services;

public enum OrderError
{
    OrderNotFound,
    OrderReviewChanged,
    OrderRequestConflict,
    OrderVersionConflict,
    OrderTransitionDenied,
    OrderCancellationDenied,
    OrderPaymentUnresolved,
    OrderPaymentRequired,
    OrderRecipeRequired,
    OrderIngredientInactive,
    OrderInsufficientStock,
    OrderStockQuantityExceeded,
    OrderStockReturnOverflow,
    PublicCheckoutUnavailable,
    PublicDeliveryUnavailable,
    InvalidSession,
    PermissionDenied
}

public sealed class OrderException(OrderError error, string message) : Exception(message)
{
    public OrderError Error { get; } = error;
}
