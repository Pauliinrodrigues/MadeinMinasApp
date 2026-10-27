using System.ComponentModel.DataAnnotations;
using MadeInMinas.Api.DTOs.StoreAttendance;

namespace MadeInMinas.Api.Services;

public sealed class StoreAttendanceOptions
{
    public StoreOpeningHours[] Hours { get; set; } = [];
    public string[] PaymentMethods { get; set; } = [];
    public string DeliveryEstimateMessage { get; set; } = "";
    public bool IsPaused { get; set; }
    public bool ReceiveOrdersOutsideHours { get; set; }

    public StoreAttendanceData Data() => new(Hours, PaymentMethods, DeliveryEstimateMessage, IsPaused, ReceiveOrdersOutsideHours);
    public bool IsValid()
    {
        var data = Data();
        return Validator.TryValidateObject(data, new ValidationContext(data), [], true)
            && Hours.All(hour => hour is not null && Validator.TryValidateObject(hour, new ValidationContext(hour), [], true));
    }
}
