using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Infrastructure;

public sealed class IngredientExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not IngredientException failure) return false;
        var status = failure.Error switch
        {
            IngredientError.IngredientNotFound => StatusCodes.Status404NotFound,
            IngredientError.DuplicateIngredientName or IngredientError.IngredientUnitImmutable => StatusCodes.Status409Conflict,
            IngredientError.InvalidSession => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status403Forbidden
        };
        context.Response.StatusCode = status;
        if (status == StatusCodes.Status401Unauthorized) context.Response.Headers.WWWAuthenticate = "Bearer";
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status, Title = failure.Message,
                Extensions = { ["code"] = failure.Error.ToString() }
            }
        });
    }
}
