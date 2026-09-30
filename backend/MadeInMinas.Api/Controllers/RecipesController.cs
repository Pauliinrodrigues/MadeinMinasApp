using System.Security.Claims;
using MadeInMinas.Api.DTOs.Recipes;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/products/{productId:guid}/recipe")]
[Authorize(Policy = AccessPolicies.ManageCatalog)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RecipesController(RecipeService recipes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RecipeResponse>> Get(Guid productId, CancellationToken cancellationToken) =>
        await recipes.GetAsync(productId, cancellationToken);

    [HttpPut]
    public async Task<ActionResult<RecipeResponse>> Save(Guid productId, RecipeRequest request, CancellationToken cancellationToken)
    {
        var result = await recipes.SaveAsync(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("auth_stamp")!),
            productId, request, cancellationToken);
        return result.Created ? CreatedAtAction(nameof(Get), new
        {
            productId
        }, result.Recipe) : Ok(result.Recipe);
    }
}
