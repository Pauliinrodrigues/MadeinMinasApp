using System.Security.Claims;
using MadeInMinas.Api.DTOs.StoreAttendance;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/store-attendance")]
[Authorize(Policy = AccessPolicies.ManageDelivery)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(8192)]
public sealed class StoreAttendanceController(StoreAttendanceService service) : ControllerBase
{
    [HttpGet]
    public Task<StoreAttendanceResponse> Get(CancellationToken cancellationToken) => service.GetAsync(cancellationToken);
    [HttpPut]
    public Task<StoreAttendanceResponse> Save(SaveStoreAttendanceRequest request, CancellationToken cancellationToken) =>
        service.SaveAsync(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("auth_stamp")!), request, cancellationToken);
    [HttpGet("public")]
    [AllowAnonymous]
    public Task<PublicStoreAttendanceResponse> Public(CancellationToken cancellationToken) => service.PublicAsync(cancellationToken);
}
