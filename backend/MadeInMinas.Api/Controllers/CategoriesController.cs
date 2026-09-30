using System.Security.Claims;
using MadeInMinas.Api.DTOs.Categories;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize(Policy = AccessPolicies.ManageCatalog)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CategoriesController(CategoryService categories) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue("sub")!);
    private Guid ActorStamp => Guid.Parse(User.FindFirstValue("auth_stamp")!);

    [HttpGet]
    public async Task<ActionResult<CategoryPageResponse>> List([FromQuery] CategoryListQuery query, CancellationToken cancellationToken) =>
        await categories.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await categories.GetAsync(id, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<CategoryResponse>> Create(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await categories.CreateAsync(ActorId, ActorStamp, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new
        {
            id = category.Id
        }, category);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> Update(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken) =>
        await categories.UpdateAsync(ActorId, ActorStamp, id, request, cancellationToken);

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<CategoryResponse>> SetStatus(Guid id, CategoryStatusRequest request, CancellationToken cancellationToken) =>
        await categories.SetStatusAsync(ActorId, ActorStamp, id, request.IsActive!.Value, cancellationToken);
}

