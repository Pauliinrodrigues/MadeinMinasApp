using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Infrastructure;

public sealed class CustomerExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CustomerException failure)
            return false;
        var status = failure.Error switch
        {
            CustomerError.CustomerNotFound or CustomerError.AddressNotFound => StatusCodes.Status404NotFound,
            CustomerError.DuplicateCustomerPhone => StatusCodes.Status409Conflict,
            CustomerError.InvalidCustomerPhone => StatusCodes.Status400BadRequest,
            CustomerError.InvalidSession => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status403Forbidden
        };
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
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
