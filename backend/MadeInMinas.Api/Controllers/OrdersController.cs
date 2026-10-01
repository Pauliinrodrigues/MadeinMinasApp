using System.Security.Claims;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Policy = AccessPolicies.ManageOrders)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OrdersController(OrderService orders) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue("sub")!);
    private Guid ActorStamp => Guid.Parse(User.FindFirstValue("auth_stamp")!);

    [HttpGet]
    public async Task<ActionResult<OrderPageResponse>> List([FromQuery] OrderListQuery query, CancellationToken cancellationToken) =>
        await orders.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await orders.GetAsync(id, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await orders.CreateAsync(ActorId, ActorStamp, request, cancellationToken);
        return result.Created ? CreatedAtAction(nameof(Get), new { id = result.Order.Id }, result.Order) : Ok(result.Order);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<OrderResponse>> SetStatus(Guid id, OrderStatusRequest request, CancellationToken cancellationToken) =>
        await orders.SetStatusAsync(ActorId, ActorStamp, id, request, cancellationToken);
}
