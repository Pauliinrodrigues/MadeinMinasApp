using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Users;
using MadeInMinas.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MadeInMinas.Api.Services;

public sealed class UserService(
    AppDbContext database,
    IPasswordHasher<User> passwordHasher,
    TimeProvider clock,
    ILogger<UserService> logger)
{
    public async Task<UserPageResponse> ListAsync(UserListQuery request, CancellationToken cancellationToken)
    {
        var query = database.Users.AsNoTracking();
        if (request.IsActive is not null)
            query = query.Where(user => user.IsActive == request.IsActive);
        if (request.RoleId is not null)
            query = query.Where(user => user.RoleId == request.RoleId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            query = query.Where(user => user.NormalizedUsername.Contains(search) || user.Name.ToUpper().Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(user => user.NormalizedUsername).ThenBy(user => user.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(user => new UserResponse(user.Id, user.Name, user.Username, user.RoleId,
                user.Role.Code, user.IsActive, user.CreatedAt))
            .ToArrayAsync(cancellationToken);
        return new UserPageResponse(items, request.Page, request.PageSize, total);
    }

    public async Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await database.Users.AsNoTracking().Where(user => user.Id == id)
            .Select(user => new UserResponse(user.Id, user.Name, user.Username, user.RoleId,
                user.Role.Code, user.IsActive, user.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new UserManagementException(UserManagementError.UserNotFound, "Funcionário não encontrado.");

    public async Task<UserResponse> CreateAsync(
        Guid actorId, Guid actorStamp, CreateUserRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, true, cancellationToken);
        await RequireRoleAsync(request.RoleId, cancellationToken);
        var normalized = request.Username.ToUpperInvariant();
        await EnsureUniqueUsernameAsync(normalized, null, cancellationToken);
        var user = new User
        {
            Name = request.Name.Trim(),
            Username = request.Username,
            NormalizedUsername = normalized,
            RoleId = request.RoleId,
            IsActive = request.IsActive,
            CreatedAt = clock.GetUtcNow()
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        database.Users.Add(user);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Staff user {UserId} created by {ActorId}.", user.Id, actorId);
        return await GetAsync(user.Id, cancellationToken);
    }

    public async Task<UserResponse> UpdateAsync(
        Guid actorId, Guid actorStamp, Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, true, cancellationToken);
        var user = await RequireUserForUpdateAsync(id, cancellationToken);
        await RequireRoleAsync(request.RoleId, cancellationToken);
        var normalized = request.Username.ToUpperInvariant();
        await EnsureUniqueUsernameAsync(normalized, id, cancellationToken);
        await EnsureAdministratorRemainsAsync(user, request.RoleId, request.IsActive!.Value, cancellationToken);
        if (user.Username != request.Username || user.RoleId != request.RoleId || user.IsActive != request.IsActive)
            user.SecurityStamp = Guid.NewGuid();
        user.Name = request.Name.Trim();
        user.Username = request.Username;
        user.NormalizedUsername = normalized;
        user.RoleId = request.RoleId;
        user.IsActive = request.IsActive.Value;
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Staff user {UserId} updated by {ActorId}.", id, actorId);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<UserResponse> SetStatusAsync(
        Guid actorId, Guid actorStamp, Guid id, bool isActive, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, true, cancellationToken);
        var user = await RequireUserForUpdateAsync(id, cancellationToken);
        await EnsureAdministratorRemainsAsync(user, user.RoleId, isActive, cancellationToken);
        if (user.IsActive != isActive)
            user.SecurityStamp = Guid.NewGuid();
        user.IsActive = isActive;
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Staff user {UserId} active status set to {IsActive} by {ActorId}.", id, isActive, actorId);
        return await GetAsync(id, cancellationToken);
    }

    public async Task ResetPasswordAsync(
        Guid actorId, Guid actorStamp, Guid id, string newPassword, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, true, cancellationToken);
        if (actorId == id)
            throw new UserManagementException(UserManagementError.UseOwnPasswordEndpoint,
                "Para alterar sua própria senha, informe a senha atual em /api/auth/password.");
        var user = await RequireUserForUpdateAsync(id, cancellationToken);
        SetPassword(user, newPassword);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Staff user {UserId} password reset by {ActorId}.", id, actorId);
    }

    public async Task ChangePasswordAsync(
        Guid actorId, Guid actorStamp, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, false, cancellationToken);
        var user = await RequireUserForUpdateAsync(actorId, cancellationToken);
        var now = clock.GetUtcNow();
        if (user.LockoutEndAt > now)
            throw new UserManagementException(UserManagementError.InvalidPassword, "Não foi possível validar a senha atual.");
        if (user.LockoutEndAt is not null)
        {
            user.LockoutEndAt = null;
            user.FailedLoginAttempts = 0;
        }
        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= 5)
                user.LockoutEndAt = now.AddMinutes(15);
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new UserManagementException(UserManagementError.InvalidPassword, "Não foi possível validar a senha atual.");
        }

        SetPassword(user, request.NewPassword);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Staff user {UserId} changed their password.", actorId);
    }

    private void SetPassword(User user, string password)
    {
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid();
        user.FailedLoginAttempts = 0;
        user.LockoutEndAt = null;
    }

    private async Task<IDbContextTransaction> BeginWriteAsync(
        Guid actorId, Guid actorStamp, bool requireAdministrator, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Mesmo lock do bootstrap: serializa mudancas que afetam o conjunto de administradores.
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(48151623)", cancellationToken);
            var actor = await database.Users
                .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {actorId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (actor is null || !actor.IsActive || actor.SecurityStamp != actorStamp)
                throw new UserManagementException(UserManagementError.InvalidSession, "Sessão inválida. Faça login novamente.");
            if (requireAdministrator && !await database.Roles.AnyAsync(
                role => role.Id == actor.RoleId && role.Code == "Administrator", cancellationToken))
                throw new UserManagementException(UserManagementError.PermissionDenied, "Acesso restrito ao administrador.");
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private async Task<User> RequireUserForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        await database.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new UserManagementException(UserManagementError.UserNotFound, "Funcionário não encontrado.");

    private async Task RequireRoleAsync(int roleId, CancellationToken cancellationToken)
    {
        if (!await database.Roles.AnyAsync(role => role.Id == roleId, cancellationToken))
            throw new UserManagementException(UserManagementError.InvalidRole, "Perfil de acesso inválido.");
    }

    private async Task EnsureUniqueUsernameAsync(string normalized, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (await database.Users.AnyAsync(user => user.NormalizedUsername == normalized && user.Id != exceptId, cancellationToken))
            throw new UserManagementException(UserManagementError.DuplicateUsername, "Este login já está em uso.");
    }

    private async Task EnsureAdministratorRemainsAsync(
        User user, int newRoleId, bool newIsActive, CancellationToken cancellationToken)
    {
        var administratorId = await database.Roles.Where(role => role.Code == "Administrator")
            .Select(role => role.Id).SingleAsync(cancellationToken);
        if (user.IsActive && user.RoleId == administratorId && (!newIsActive || newRoleId != administratorId) &&
            !await database.Users.AnyAsync(other => other.Id != user.Id && other.IsActive && other.RoleId == administratorId, cancellationToken))
            throw new UserManagementException(UserManagementError.LastAdministrator,
                "É necessário manter pelo menos um administrador ativo.");
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Users_NormalizedUsername" })
        {
            throw new UserManagementException(UserManagementError.DuplicateUsername, "Este login já está em uso.");
        }
    }
}
