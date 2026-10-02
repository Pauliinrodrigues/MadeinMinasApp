using MadeInMinas.Api.DTOs.Dashboard;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = AccessPolicies.ViewDashboard)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet("today")]
    public async Task<ActionResult<DailyDashboardResponse>> Today(CancellationToken cancellationToken) =>
        await dashboard.TodayAsync(cancellationToken);
}
