using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class AdministratorBootstrap(AppDbContext database, IPasswordHasher<User> hasher)
{
    public async Task CreateAsync(string name, string username, string password, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        username = username.Trim();
        if (name.Length is < 1 or > 120 || !Regex.IsMatch(username, @"\A[a-zA-Z0-9._-]{3,64}\z") ||
            password.Length is < 15 or > 128 || string.IsNullOrWhiteSpace(password))
            throw new ValidationException("Nome: 1 a 120 caracteres; login: 3 a 64 letras, numeros, ponto, hifen ou sublinhado; senha: 15 a 128 caracteres.");

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // O lock transacional impede dois bootstraps simultaneos.
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(48151623)", cancellationToken);
        if (await database.Users.AnyAsync(cancellationToken))
            throw new InvalidOperationException("O administrador inicial so pode ser criado quando nao existem usuarios.");

        var role = await database.Roles.SingleAsync(role => role.Code == "Administrator", cancellationToken);
        var user = new User
        {
            Name = name,
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            RoleId = role.Id
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        database.Users.Add(user);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
