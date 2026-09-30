using MadeInMinas.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MadeInMinas.Api.Services;

internal static class CatalogWriteTransaction
{
    public static async Task<IDbContextTransaction> BeginAsync(
        AppDbContext database,
        Guid actorId,
        Guid actorStamp,
        Func<Exception> invalidSession,
        Func<Exception> permissionDenied,
        CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Mantém o usuário bloqueado contra revogação até o commit da gravação.
            var actor = await database.Users.FromSqlInterpolated(
                    $"SELECT * FROM \"Users\" WHERE \"Id\" = {actorId} FOR SHARE")
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);

            if (actor is null || !actor.IsActive || actor.SecurityStamp != actorStamp)
            {
                throw invalidSession();
            }

            var isAdministrator = await database.Roles.AnyAsync(
                role => role.Id == actor.RoleId && role.Code == "Administrator", cancellationToken);
            if (!isAdministrator)
            {
                throw permissionDenied();
            }

            // O chamador mantém a transação aberta durante a validação e a persistência.
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
