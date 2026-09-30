using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Infrastructure;

public sealed class ProductExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ProductException failure)
            return false;
        var status = failure.Error switch
        {
            ProductError.ProductNotFound => StatusCodes.Status404NotFound,
            ProductError.DuplicateProductName => StatusCodes.Status409Conflict,
            ProductError.InvalidSession => StatusCodes.Status401Unauthorized,
            ProductError.PermissionDenied => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
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

