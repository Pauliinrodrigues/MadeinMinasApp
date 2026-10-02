using MadeInMinas.Api.DTOs.Reports;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = AccessPolicies.ViewReports)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReportsController(SalesReportService reports) : ControllerBase
{
    [HttpGet("sales")]
    public async Task<ActionResult<SalesReportResponse>> Sales([FromQuery] SalesReportQuery query, CancellationToken cancellationToken) =>
        await reports.GetAsync(query, cancellationToken);
}
