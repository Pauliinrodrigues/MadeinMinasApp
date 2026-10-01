using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Infrastructure;

public sealed class PaymentExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not PaymentException failure)
            return false;
        var status = failure.Error switch
        {
            PaymentError.PaymentNotFound or PaymentError.PaymentOrderNotFound => StatusCodes.Status404NotFound,
            PaymentError.InvalidSession => StatusCodes.Status401Unauthorized,
            PaymentError.PermissionDenied => StatusCodes.Status403Forbidden,
            PaymentError.PaymentInvalidCash => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status409Conflict
        };
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        if (status == StatusCodes.Status401Unauthorized)
            context.Response.Headers.WWWAuthenticate = "Bearer";
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = failure.Message, Extensions = { ["code"] = failure.Error.ToString() } }
        });
    }
}
