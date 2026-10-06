using System.Security.Claims;
using MadeInMinas.Api.DTOs.Delivery;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/delivery-settings")]
[Authorize(Policy = AccessPolicies.ManageDelivery)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(524288)]
public sealed class DeliverySettingsController(DeliverySettingsService settings) : ControllerBase
{
    [HttpGet]
    public Task<DeliverySettingsResponse> Get(CancellationToken cancellationToken) => settings.GetAsync(cancellationToken);

    [HttpPut]
    public Task<DeliverySettingsResponse> Save(SaveDeliverySettingsRequest request, CancellationToken cancellationToken) =>
        settings.SaveAsync(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("auth_stamp")!), request, cancellationToken);
}
