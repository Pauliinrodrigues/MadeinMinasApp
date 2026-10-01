using System.Security.Claims;
using MadeInMinas.Api.DTOs.Payments;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/orders/{orderId:guid}/payments")]
[Authorize(Policy = AccessPolicies.ManagePayments)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PaymentsController(PaymentService payments) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue("sub")!);
    private Guid ActorStamp => Guid.Parse(User.FindFirstValue("auth_stamp")!);

    [HttpGet]
    public async Task<ActionResult<PaymentPageResponse>> List(Guid orderId, [FromQuery] PaymentListQuery query, CancellationToken cancellationToken) =>
        await payments.ListAsync(orderId, query, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PaymentResponse>> Get(Guid orderId, Guid id, CancellationToken cancellationToken) =>
        await payments.GetAsync(orderId, id, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<PaymentResponse>> Create(Guid orderId, CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await payments.CreateAsync(ActorId, ActorStamp, orderId, request, cancellationToken);
        return result.Created ? CreatedAtAction(nameof(Get), new { orderId, id = result.Payment.Id }, result.Payment) : Ok(result.Payment);
    }

    [HttpPut("{id:guid}/receive")]
    public async Task<ActionResult<PaymentResponse>> Receive(Guid orderId, Guid id, ReceivePaymentRequest request, CancellationToken cancellationToken) =>
        await payments.ReceiveAsync(ActorId, ActorStamp, orderId, id, request, cancellationToken);

    [HttpPut("{id:guid}/cancel")]
    public async Task<ActionResult<PaymentResponse>> Cancel(Guid orderId, Guid id, CancelPaymentRequest request, CancellationToken cancellationToken) =>
        await payments.CancelAsync(ActorId, ActorStamp, orderId, id, request, cancellationToken);

    [HttpPut("{id:guid}/refund")]
    [Authorize(Policy = AccessPolicies.RefundPayments)]
    public async Task<ActionResult<PaymentResponse>> Refund(Guid orderId, Guid id, RefundPaymentRequest request, CancellationToken cancellationToken) =>
        await payments.RefundAsync(ActorId, ActorStamp, orderId, id, request, cancellationToken);
}
