namespace MadeInMinas.Api.Services;

public enum DeliverySettingsError { DeliverySettingsChanged, DeliveryAreaRemovalDenied, InvalidSession, PermissionDenied, FixedDeliveryFeeRequired }

public sealed class DeliverySettingsException(DeliverySettingsError error, string message) : Exception(message)
{
    public DeliverySettingsError Error { get; } = error;
}
