using System.Text;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Categories;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MadeInMinas.Api.Services;

public sealed class CategoryService(AppDbContext database, TimeProvider clock, ILogger<CategoryService> logger)
{
    public async Task<CategoryPageResponse> ListAsync(CategoryListQuery request, CancellationToken cancellationToken)
    {
        var query = database.Categories.AsNoTracking();
        if (request.IsActive is not null) query = query.Where(category => category.IsActive == request.IsActive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = NormalizeName(request.Search);
            query = query.Where(category => category.NormalizedName.Contains(search));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(category => category.DisplayOrder).ThenBy(category => category.NormalizedName)
            .ThenBy(category => category.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(category => new CategoryResponse(category.Id, category.Name, category.Description,
                category.DisplayOrder, category.IsActive, category.CreatedAt, category.UpdatedAt))
            .ToArrayAsync(cancellationToken);
        return new CategoryPageResponse(items, request.Page, request.PageSize, total);
    }

    public async Task<CategoryResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await database.Categories.AsNoTracking().SingleOrDefaultAsync(category => category.Id == id, cancellationToken)
            ?? throw NotFound();
        return ToResponse(category);
    }

    public async Task<CategoryResponse> CreateAsync(
        Guid actorId, Guid actorStamp, CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var now = UtcNow();
        var category = new Category
        {
            Name = request.Name.Trim().Normalize(NormalizationForm.FormC),
            NormalizedName = NormalizeName(request.Name),
            Description = CleanDescription(request.Description), DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive, CreatedAt = now, UpdatedAt = now
        };
        database.Categories.Add(category);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Category {CategoryId} created by {ActorId}.", category.Id, actorId);
        return ToResponse(category);
    }

    public async Task<CategoryResponse> UpdateAsync(
        Guid actorId, Guid actorStamp, Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var category = await RequireForUpdateAsync(id, cancellationToken);
        category.Name = request.Name.Trim().Normalize(NormalizationForm.FormC);
        category.NormalizedName = NormalizeName(request.Name);
        category.Description = CleanDescription(request.Description);
        category.DisplayOrder = request.DisplayOrder!.Value;
        category.IsActive = request.IsActive!.Value;
        category.UpdatedAt = UtcNow();
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Category {CategoryId} updated by {ActorId}.", id, actorId);
        return ToResponse(category);
    }

    public async Task<CategoryResponse> SetStatusAsync(
        Guid actorId, Guid actorStamp, Guid id, bool isActive, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var category = await RequireForUpdateAsync(id, cancellationToken);
        if (category.IsActive != isActive)
        {
            category.IsActive = isActive;
            category.UpdatedAt = UtcNow();
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Category {CategoryId} active status set to {IsActive} by {ActorId}.", id, isActive, actorId);
        return ToResponse(category);
    }

    private async Task<IDbContextTransaction> BeginWriteAsync(Guid actorId, Guid actorStamp, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Bloqueia revogação/alteração do autor até o término desta gravação.
            var actor = await database.Users.FromSqlInterpolated(
                $"SELECT * FROM \"Users\" WHERE \"Id\" = {actorId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (actor is null || !actor.IsActive || actor.SecurityStamp != actorStamp)
                throw new CategoryException(CategoryError.InvalidSession, "Sessão inválida. Faça login novamente.");
            if (!await database.Roles.AnyAsync(role => role.Id == actor.RoleId && role.Code == "Administrator", cancellationToken))
                throw new CategoryException(CategoryError.PermissionDenied, "Acesso restrito ao administrador.");
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private async Task<Category> RequireForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        await database.Categories.FromSqlInterpolated(
            $"SELECT * FROM \"Categories\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
        ?? throw NotFound();

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Categories_NormalizedName" })
        {
            throw new CategoryException(CategoryError.DuplicateCategoryName, "Já existe uma categoria com esse nome, inclusive entre as inativas.");
        }
    }

    private static string NormalizeName(string value) => value.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    // Mantém a mesma precisão na resposta da gravação e na leitura do PostgreSQL.
    private DateTimeOffset UtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
    private static string? CleanDescription(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static CategoryException NotFound() => new(CategoryError.CategoryNotFound, "Categoria não encontrada.");
    private static CategoryResponse ToResponse(Category category) => new(category.Id, category.Name, category.Description,
        category.DisplayOrder, category.IsActive, category.CreatedAt, category.UpdatedAt);
}
