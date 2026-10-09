using System.ComponentModel.DataAnnotations;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MadeInMinas.Api.Controllers;

public sealed record PrintingVersionRequest([Range(1, int.MaxValue)] int ExpectedVersion);
public sealed record PrintingAutomaticRequest(bool Automatic, [Range(1, int.MaxValue)] int ExpectedVersion);
public sealed record PrintManualRequest(Guid RequestId, [Range(1, int.MaxValue)] int ExpectedVersion);
public sealed record PrintFinishRequest(Guid ClaimId, bool Submitted);

[ApiController]
[Route("api/printing")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(4096)]
public sealed class PrintQueueController(PrintQueueService queue) : ControllerBase
{
    [HttpGet("settings")]
    [Authorize(Policy = AccessPolicies.ManagePrinting)]
    public Task<PrintSettingsResponse> Settings(CancellationToken ct) => queue.GetSettingsAsync(ct);

    [HttpPost("station")]
    [Authorize(Policy = AccessPolicies.ManagePrinting)]
    public async Task<IActionResult> Register(PrintingVersionRequest request, CancellationToken ct)
    {
        var result = await queue.RegisterAsync(request.ExpectedVersion, ct);
        return Ok(new { settings = result.Settings, token = result.Key });
    }

    [HttpPut("settings")]
    [Authorize(Policy = AccessPolicies.ManagePrinting)]
    public Task<PrintSettingsResponse> Automatic(PrintingAutomaticRequest request, CancellationToken ct) =>
        queue.SetAutomaticAsync(request.Automatic, request.ExpectedVersion, ct);

    [HttpGet("jobs")]
    [Authorize(Policy = AccessPolicies.ManagePrinting)]
    public Task<PrintJobResponse[]> Jobs(CancellationToken ct) => queue.ListAsync(ct);

    [HttpPost("jobs/{id:guid}/cancel")]
    [Authorize(Policy = AccessPolicies.ManagePrinting)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await queue.CancelJobAsync(id, ct);
        return NoContent();
    }

    [HttpPost("orders/{id:guid}/kitchen")]
    [Authorize(Policy = AccessPolicies.PrintKitchen)]
    public Task<PrintJobResponse> Kitchen(Guid id, PrintManualRequest request, CancellationToken ct) => Manual(id, "kitchen", request, ct);

    [HttpPost("orders/{id:guid}/dispatch")]
    [Authorize(Policy = AccessPolicies.PrintDispatch)]
    public Task<PrintJobResponse> Dispatch(Guid id, PrintManualRequest request, CancellationToken ct) => Manual(id, "dispatch", request, ct);

    private Task<PrintJobResponse> Manual(Guid id, string mode, PrintManualRequest request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty)
            throw new PrintingException(400, "Identificador da solicitação obrigatório.");
        return queue.ManualAsync(id, mode, request.RequestId, request.ExpectedVersion, ct);
    }

    // Estes dois endpoints usam a credencial revogável da estação, nunca o token de um funcionário.
    [HttpPost("agent/claim")]
    [AllowAnonymous]
    [EnableRateLimiting("print-agent")]
    public async Task<IActionResult> Claim([FromHeader(Name = "X-Print-Key")] string? key, CancellationToken ct)
    {
        var result = await queue.ClaimAsync(key ?? "", ct);
        return result is null ? NoContent() : Ok(result);
    }

    [HttpPost("agent/jobs/{id:guid}/finish")]
    [AllowAnonymous]
    [EnableRateLimiting("print-agent")]
    public async Task<IActionResult> Finish(Guid id, PrintFinishRequest request,
        [FromHeader(Name = "X-Print-Key")] string? key, CancellationToken ct)
    {
        await queue.FinishAsync(key ?? "", id, request.ClaimId, request.Submitted, ct);
        return NoContent();
    }
}
