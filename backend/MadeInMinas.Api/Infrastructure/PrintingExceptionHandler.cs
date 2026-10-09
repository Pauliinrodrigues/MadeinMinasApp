using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Infrastructure;

public sealed class PrintingExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not PrintingException failure)
            return false;
        context.Response.StatusCode = failure.Status;
        context.Response.Headers.CacheControl = "no-store";
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = failure.Status, Title = failure.Message, Extensions = { ["code"] = "PrintingUnavailable" } }
        });
    }
}
