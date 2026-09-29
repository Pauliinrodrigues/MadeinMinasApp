using System.Security.Claims;
using MadeInMinas.Api.DTOs.Ingredients;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/ingredients")]
[Authorize(Policy = AccessPolicies.ManageCatalog)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IngredientsController(IngredientService ingredients) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue("sub")!);
    private Guid ActorStamp => Guid.Parse(User.FindFirstValue("auth_stamp")!);

    [HttpGet]
    public async Task<ActionResult<IngredientPageResponse>> List([FromQuery] IngredientListQuery query, CancellationToken cancellationToken) =>
        await ingredients.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<IngredientResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await ingredients.GetAsync(id, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<IngredientResponse>> Create(IngredientRequest request, CancellationToken cancellationToken)
    {
        var ingredient = await ingredients.CreateAsync(ActorId, ActorStamp, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = ingredient.Id }, ingredient);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<IngredientResponse>> Update(Guid id, IngredientRequest request, CancellationToken cancellationToken) =>
        await ingredients.UpdateAsync(ActorId, ActorStamp, id, request, cancellationToken);

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<IngredientResponse>> SetStatus(Guid id, IngredientStatusRequest request, CancellationToken cancellationToken) =>
        await ingredients.SetStatusAsync(ActorId, ActorStamp, id, request.IsActive!.Value, cancellationToken);
}
