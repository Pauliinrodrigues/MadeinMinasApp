namespace MadeInMinas.Api.Services;

public enum PaymentError
{
    PaymentNotFound,
    PaymentOrderNotFound,
    PaymentOrderCancelled,
    PaymentOrderChanged,
    PaymentAlreadyActive,
    PaymentRequestConflict,
    PaymentVersionConflict,
    PaymentTransitionDenied,
    PaymentInvalidCash,
    InvalidSession,
    PermissionDenied
}

public sealed class PaymentException(PaymentError error, string message) : Exception(message)
{
    public PaymentError Error { get; } = error;
}
