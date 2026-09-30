using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Infrastructure;

public sealed class RecipeExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not RecipeException failure)
            return false;
        var status = failure.Error switch
        {
            RecipeError.ProductNotFound or RecipeError.RecipeNotFound => StatusCodes.Status404NotFound,
            RecipeError.InvalidRecipeIngredient or RecipeError.InactiveRecipeIngredient => StatusCodes.Status400BadRequest,
            RecipeError.InvalidSession => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status403Forbidden
        };
        context.Response.StatusCode = status;
        if (status == StatusCodes.Status401Unauthorized)
            context.Response.Headers.WWWAuthenticate = "Bearer";
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = failure.Message, Extensions = { ["code"] = failure.Error.ToString() } }
        });
    }
}
