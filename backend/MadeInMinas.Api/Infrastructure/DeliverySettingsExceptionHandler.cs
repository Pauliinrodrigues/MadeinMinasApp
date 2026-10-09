using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Infrastructure;

public sealed class DeliverySettingsExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DeliverySettingsException failure)
            return false;
        var status = failure.Error switch
        {
            DeliverySettingsError.InvalidSession => StatusCodes.Status401Unauthorized,
            DeliverySettingsError.PermissionDenied => StatusCodes.Status403Forbidden,
            DeliverySettingsError.FixedDeliveryFeeRequired => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status409Conflict
        };
        context.Response.StatusCode = status;
        if (status == StatusCodes.Status401Unauthorized)
            context.Response.Headers.WWWAuthenticate = "Bearer";
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = failure.Message,
                Extensions = { ["code"] = failure.Error.ToString() }
            }
        });
    }
}
