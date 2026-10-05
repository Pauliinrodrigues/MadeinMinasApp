using System.Data;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Menu;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class PublicMenuService(AppDbContext database)
{
    public async Task<PublicMenuResponse> GetAsync(PublicMenuQuery request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var products = database.Products.AsNoTracking().Where(product => product.IsActive && product.Category.IsActive);
        var categories = await database.Categories.AsNoTracking()
            .Where(category => category.IsActive && products.Any(product => product.CategoryId == category.Id))
            .OrderBy(category => category.DisplayOrder).ThenBy(category => category.NormalizedName).ThenBy(category => category.Id)
            .Select(category => new MenuCategoryResponse(category.Id, category.Name)).ToArrayAsync(cancellationToken);
        if (request.CategoryId is not null)
            products = products.Where(product => product.CategoryId == request.CategoryId);
        var total = await products.CountAsync(cancellationToken);
        // A pausa de venda continua visível. Inativos e dados administrativos não fazem parte do contrato público.
        var items = await products.OrderBy(product => product.Category.DisplayOrder)
            .ThenBy(product => product.Category.NormalizedName).ThenBy(product => product.CategoryId)
            .ThenBy(product => product.NormalizedName).ThenBy(product => product.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(product => new MenuProductResponse(product.Id, product.CategoryId, product.Name,
                product.Description, product.Price, product.ImageUrl, product.IsAvailable)).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PublicMenuResponse(categories, items, request.Page, request.PageSize, total);
    }
}
