using System.Security.Claims;
using MadeInMinas.Api.DTOs.Customers;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = AccessPolicies.ManageCustomers)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomersController(CustomerService customers) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue("sub")!);
    private Guid ActorStamp => Guid.Parse(User.FindFirstValue("auth_stamp")!);

    [HttpGet]
    public async Task<ActionResult<CustomerPageResponse>> List([FromQuery] CustomerListQuery query, CancellationToken cancellationToken) =>
        await customers.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await customers.GetAsync(id, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(CustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await customers.CreateAsync(ActorId, ActorStamp, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Update(Guid id, CustomerRequest request, CancellationToken cancellationToken) =>
        await customers.UpdateAsync(ActorId, ActorStamp, id, request, cancellationToken);

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<CustomerResponse>> SetStatus(Guid id, CustomerStatusRequest request, CancellationToken cancellationToken) =>
        await customers.SetStatusAsync(ActorId, ActorStamp, id, request.IsActive!.Value, cancellationToken);

    [HttpGet("{customerId:guid}/addresses")]
    public async Task<ActionResult<AddressPageResponse>> ListAddresses(Guid customerId, [FromQuery] AddressListQuery query, CancellationToken cancellationToken) =>
        await customers.ListAddressesAsync(customerId, query, cancellationToken);

    [HttpGet("{customerId:guid}/addresses/{addressId:guid}")]
    public async Task<ActionResult<AddressResponse>> GetAddress(Guid customerId, Guid addressId, CancellationToken cancellationToken) =>
        await customers.GetAddressAsync(customerId, addressId, cancellationToken);

    [HttpPost("{customerId:guid}/addresses")]
    public async Task<ActionResult<AddressResponse>> CreateAddress(Guid customerId, AddressRequest request, CancellationToken cancellationToken)
    {
        var address = await customers.CreateAddressAsync(ActorId, ActorStamp, customerId, request, cancellationToken);
        return CreatedAtAction(nameof(GetAddress), new { customerId, addressId = address.Id }, address);
    }

    [HttpPut("{customerId:guid}/addresses/{addressId:guid}")]
    public async Task<ActionResult<AddressResponse>> UpdateAddress(Guid customerId, Guid addressId, AddressRequest request, CancellationToken cancellationToken) =>
        await customers.UpdateAddressAsync(ActorId, ActorStamp, customerId, addressId, request, cancellationToken);

    [HttpPut("{customerId:guid}/addresses/{addressId:guid}/status")]
    public async Task<ActionResult<AddressResponse>> SetAddressStatus(Guid customerId, Guid addressId, CustomerStatusRequest request, CancellationToken cancellationToken) =>
        await customers.SetAddressStatusAsync(ActorId, ActorStamp, customerId, addressId, request.IsActive!.Value, cancellationToken);
}
