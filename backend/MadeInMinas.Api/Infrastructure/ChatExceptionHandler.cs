using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Infrastructure;

public sealed class ChatExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ChatException failure)
            return false;
        var status = failure.Error == ChatError.ChatUnavailable ? 404 : 409;
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = failure.Message, Extensions = { ["code"] = failure.Error.ToString() } }
        });
    }
}
