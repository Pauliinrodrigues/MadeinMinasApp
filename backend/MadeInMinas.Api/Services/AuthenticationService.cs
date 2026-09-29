using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MadeInMinas.Api.Services;

public sealed class AuthenticationService(
    AppDbContext database,
    IPasswordHasher<User> passwordHasher,
    IOptions<JwtSettings> settings,
    TimeProvider clock)
{
    private static readonly User DummyUser = new();
    private static readonly string DummyHash = new PasswordHasher<User>(
        Options.Create(new PasswordHasherOptions { IterationCount = 210_000 })).HashPassword(
        DummyUser, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedUsername = request.Username.ToUpperInvariant();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Serializa tentativas da mesma conta para nao perder incrementos concorrentes.
        var user = await database.Users
            .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"NormalizedUsername\" = {normalizedUsername} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        var now = clock.GetUtcNow();
        if (user is null || !user.IsActive || user.LockoutEndAt > now)
        {
            passwordHasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
            return null;
        }

        if (user.LockoutEndAt is not null)
        {
            user.LockoutEndAt = null;
            user.FailedLoginAttempts = 0;
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= 5)
                user.LockoutEndAt = now.AddMinutes(15);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        user.FailedLoginAttempts = 0;
        user.LockoutEndAt = null;
        var role = await database.Roles.SingleAsync(role => role.Id == user.RoleId, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        var jwt = settings.Value;
        var expiresAt = now.AddMinutes(jwt.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim("sub", user.Id.ToString()),
            new Claim("role", role.Code),
            new Claim("auth_stamp", user.SecurityStamp.ToString()),
            new Claim("jti", Guid.NewGuid().ToString()),
            new Claim("iat", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience, claims,
            now.UtcDateTime, expiresAt.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256));
        var response = new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expiresAt,
            ToResponse(user, role.Code));
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<AuthenticatedUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await database.Users.AsNoTracking().Include(user => user.Role)
            .SingleOrDefaultAsync(user => user.Id == userId && user.IsActive, cancellationToken);
        return user is null ? null : ToResponse(user, user.Role.Code);
    }

    public async Task LogoutAsync(Guid userId, CancellationToken cancellationToken)
    {
        var stamp = Guid.NewGuid();
        await database.Users.Where(user => user.Id == userId)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.SecurityStamp, stamp), cancellationToken);
    }

    private static AuthenticatedUserResponse ToResponse(User user, string role) =>
        new(user.Id, user.Name, user.Username, role, AccessPolicies.ForRole(role));
}
