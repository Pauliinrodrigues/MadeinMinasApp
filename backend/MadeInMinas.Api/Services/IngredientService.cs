using System.Text;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Ingredients;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MadeInMinas.Api.Services;

public sealed class IngredientService(AppDbContext database, TimeProvider clock, ILogger<IngredientService> logger)
{
    public async Task<IngredientPageResponse> ListAsync(IngredientListQuery request, CancellationToken cancellationToken)
    {
        var query = database.Ingredients.AsNoTracking();
        if (request.IsActive is not null) query = query.Where(ingredient => ingredient.IsActive == request.IsActive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = NormalizeName(request.Search);
            query = query.Where(ingredient => ingredient.NormalizedName.Contains(search));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(ingredient => ingredient.NormalizedName)
            .ThenBy(ingredient => ingredient.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(ingredient => new IngredientResponse(ingredient.Id, ingredient.Name, ingredient.Unit,
                ingredient.UnitCost, ingredient.MinimumStock, ingredient.Supplier, ingredient.IsActive, ingredient.CreatedAt, ingredient.UpdatedAt))
            .ToArrayAsync(cancellationToken);
        return new IngredientPageResponse(items, request.Page, request.PageSize, total);
    }

    public async Task<IngredientResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var ingredient = await database.Ingredients.AsNoTracking().SingleOrDefaultAsync(ingredient => ingredient.Id == id, cancellationToken)
            ?? throw NotFound();
        return ToResponse(ingredient);
    }

    public async Task<IngredientResponse> CreateAsync(
        Guid actorId, Guid actorStamp, IngredientRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var now = UtcNow();
        var ingredient = new Ingredient
        {
            Name = request.Name.Trim().Normalize(NormalizationForm.FormC),
            NormalizedName = NormalizeName(request.Name),
            Unit = request.Unit, UnitCost = request.UnitCost!.Value, MinimumStock = request.MinimumStock!.Value,
            Supplier = CleanSupplier(request.Supplier),
            IsActive = request.IsActive!.Value, CreatedAt = now, UpdatedAt = now
        };
        database.Ingredients.Add(ingredient);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Ingredient {IngredientId} created by {ActorId}.", ingredient.Id, actorId);
        return ToResponse(ingredient);
    }

    public async Task<IngredientResponse> UpdateAsync(
        Guid actorId, Guid actorStamp, Guid id, IngredientRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var ingredient = await RequireForUpdateAsync(id, cancellationToken);
        if (ingredient.Unit != request.Unit)
            throw new IngredientException(IngredientError.IngredientUnitImmutable, "A unidade não pode ser alterada após o cadastro.");
        ingredient.Name = request.Name.Trim().Normalize(NormalizationForm.FormC);
        ingredient.NormalizedName = NormalizeName(request.Name);
        ingredient.Supplier = CleanSupplier(request.Supplier);
        ingredient.UnitCost = request.UnitCost!.Value;
        ingredient.MinimumStock = request.MinimumStock!.Value;
        ingredient.IsActive = request.IsActive!.Value;
        ingredient.UpdatedAt = UtcNow();
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Ingredient {IngredientId} updated by {ActorId}.", id, actorId);
        return ToResponse(ingredient);
    }

    public async Task<IngredientResponse> SetStatusAsync(
        Guid actorId, Guid actorStamp, Guid id, bool isActive, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var ingredient = await RequireForUpdateAsync(id, cancellationToken);
        if (ingredient.IsActive != isActive)
        {
            ingredient.IsActive = isActive;
            ingredient.UpdatedAt = UtcNow();
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Ingredient {IngredientId} active status set to {IsActive} by {ActorId}.", id, isActive, actorId);
        return ToResponse(ingredient);
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
                throw new IngredientException(IngredientError.InvalidSession, "Sessão inválida. Faça login novamente.");
            if (!await database.Roles.AnyAsync(role => role.Id == actor.RoleId && role.Code == "Administrator", cancellationToken))
                throw new IngredientException(IngredientError.PermissionDenied, "Acesso restrito ao administrador.");
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private async Task<Ingredient> RequireForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        await database.Ingredients.FromSqlInterpolated(
            $"SELECT * FROM \"Ingredients\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
        ?? throw NotFound();

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Ingredients_NormalizedName" })
        {
            throw new IngredientException(IngredientError.DuplicateIngredientName, "Já existe um ingrediente com esse nome, inclusive entre os inativos.");
        }
    }

    private static string NormalizeName(string value) => value.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    // Mantém a mesma precisão na resposta da gravação e na leitura do PostgreSQL.
    private DateTimeOffset UtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
    private static string? CleanSupplier(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IngredientException NotFound() => new(IngredientError.IngredientNotFound, "Ingrediente não encontrado.");
    private static IngredientResponse ToResponse(Ingredient ingredient) => new(ingredient.Id, ingredient.Name, ingredient.Unit,
        ingredient.UnitCost, ingredient.MinimumStock, ingredient.Supplier, ingredient.IsActive, ingredient.CreatedAt, ingredient.UpdatedAt);
}
