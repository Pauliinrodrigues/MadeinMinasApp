using System.Text;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Products;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MadeInMinas.Api.Services;

public sealed class ProductService(AppDbContext database, TimeProvider clock, ILogger<ProductService> logger)
{
    public async Task<ProductPageResponse> ListAsync(ProductListQuery request, CancellationToken cancellationToken)
    {
        var query = database.Products.AsNoTracking();
        if (request.CategoryId is not null)
            query = query.Where(product => product.CategoryId == request.CategoryId);
        if (request.IsActive is not null)
            query = query.Where(product => product.IsActive == request.IsActive);
        if (request.IsAvailableForSale is not null)
            query = query.Where(product => (product.IsActive && product.IsAvailable && product.Category.IsActive) == request.IsAvailableForSale);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = NormalizeName(request.Search);
            query = query.Where(product => product.NormalizedName.Contains(search));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(product => product.NormalizedName).ThenBy(product => product.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(product => new ProductResponse(product.Id, product.CategoryId, product.Category.Name, product.Category.IsActive,
                product.Name, product.Description, product.Price, product.ImageUrl, product.IsActive, product.IsAvailable,
                product.IsActive && product.IsAvailable && product.Category.IsActive, product.CreatedAt, product.UpdatedAt)
            {
                HasRecipe = database.Recipes.Any(recipe => recipe.ProductId == product.Id && recipe.Items.Any()),
                HasMissingCosts = database.Recipes.Any(recipe => recipe.ProductId == product.Id &&
                    recipe.Items.Any(item => item.Ingredient.UnitCost == 0))
            })
            .ToArrayAsync(cancellationToken);
        return new ProductPageResponse(items, request.Page, request.PageSize, total);
    }

    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await database.Products.AsNoTracking().Include(product => product.Category)
            .SingleOrDefaultAsync(product => product.Id == id, cancellationToken) ?? throw NotFound();
        return ToResponse(product);
    }

    public async Task<ProductResponse> CreateAsync(Guid actorId, Guid actorStamp, ProductRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var category = await RequireCategoryAsync(request.CategoryId!.Value, true, cancellationToken);
        var now = UtcNow();
        var product = new Product { Category = category, CategoryId = category.Id, CreatedAt = now };
        Apply(product, request, now);
        database.Products.Add(product);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Product {ProductId} created by {ActorId}.", product.Id, actorId);
        return ToResponse(product);
    }

    public async Task<ProductResponse> UpdateAsync(Guid actorId, Guid actorStamp, Guid id, ProductRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var product = await RequireForUpdateAsync(id, cancellationToken);
        var category = await RequireCategoryAsync(request.CategoryId!.Value, request.CategoryId != product.CategoryId, cancellationToken);
        product.CategoryId = category.Id;
        product.Category = category;
        Apply(product, request, UtcNow());
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Product {ProductId} updated by {ActorId}.", id, actorId);
        return ToResponse(product);
    }

    public Task<ProductResponse> SetStatusAsync(Guid actorId, Guid actorStamp, Guid id, bool isActive, CancellationToken cancellationToken) =>
        SetFlagsAsync(actorId, actorStamp, id, isActive, null, cancellationToken);

    public Task<ProductResponse> SetAvailabilityAsync(Guid actorId, Guid actorStamp, Guid id, bool isAvailable, CancellationToken cancellationToken) =>
        SetFlagsAsync(actorId, actorStamp, id, null, isAvailable, cancellationToken);

    private async Task<ProductResponse> SetFlagsAsync(Guid actorId, Guid actorStamp, Guid id, bool? isActive, bool? isAvailable, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var product = await RequireForUpdateAsync(id, cancellationToken);
        product.Category = await RequireCategoryAsync(product.CategoryId, false, cancellationToken);
        if ((isActive is not null && product.IsActive != isActive) || (isAvailable is not null && product.IsAvailable != isAvailable))
        {
            product.IsActive = isActive ?? product.IsActive;
            product.IsAvailable = isAvailable ?? product.IsAvailable;
            product.UpdatedAt = UtcNow();
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Product {ProductId} flags set to active {IsActive}, available {IsAvailable} by {ActorId}.",
            id, product.IsActive, product.IsAvailable, actorId);
        return ToResponse(product);
    }

    private static void Apply(Product product, ProductRequest request, DateTimeOffset now)
    {
        product.Name = request.Name.Trim().Normalize(NormalizationForm.FormC);
        product.NormalizedName = NormalizeName(request.Name);
        product.Description = CleanOptional(request.Description);
        product.ImageUrl = CleanOptional(request.ImageUrl);
        product.Price = request.Price!.Value;
        product.IsActive = request.IsActive!.Value;
        product.IsAvailable = request.IsAvailable!.Value;
        product.UpdatedAt = now;
    }

    private async Task<IDbContextTransaction> BeginWriteAsync(Guid actorId, Guid actorStamp, CancellationToken cancellationToken) =>
        await CatalogWriteTransaction.BeginAsync(
            database, actorId, actorStamp,
            () => new ProductException(ProductError.InvalidSession, "Sessão inválida. Faça login novamente."),
            () => new ProductException(ProductError.PermissionDenied, "Acesso restrito ao administrador."),
            cancellationToken);

    private async Task<Category> RequireCategoryAsync(Guid id, bool requireActive, CancellationToken cancellationToken)
    {
        var category = await database.Categories.FromSqlInterpolated(
            $"SELECT * FROM \"Categories\" WHERE \"Id\" = {id} FOR SHARE").SingleOrDefaultAsync(cancellationToken)
            ?? throw new ProductException(ProductError.InvalidProductCategory, "Categoria não encontrada.");
        if (requireActive && !category.IsActive)
            throw new ProductException(ProductError.InactiveProductCategory, "Selecione uma categoria ativa para cadastrar ou mover o produto.");
        return category;
    }

    private async Task<Product> RequireForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        await database.Products.FromSqlInterpolated(
            $"SELECT * FROM \"Products\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken) ?? throw NotFound();

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Products_CategoryId_NormalizedName" })
        {
            throw new ProductException(ProductError.DuplicateProductName, "Já existe um produto com esse nome nesta categoria, inclusive entre os inativos.");
        }
    }

    private DateTimeOffset UtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
    private static string NormalizeName(string value) => value.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ProductException NotFound() => new(ProductError.ProductNotFound, "Produto não encontrado.");
    private static ProductResponse ToResponse(Product product) => new(product.Id, product.CategoryId, product.Category.Name,
        product.Category.IsActive, product.Name, product.Description, product.Price, product.ImageUrl, product.IsActive,
        product.IsAvailable, product.IsActive && product.IsAvailable && product.Category.IsActive, product.CreatedAt, product.UpdatedAt);
}
