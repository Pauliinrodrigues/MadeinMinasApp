using System.Threading.RateLimiting;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MadeInMinas.Api.Security;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddStaffAuthentication(this IServiceCollection services)
    {
        services.AddOptions<JwtSettings>().BindConfiguration("Jwt")
            .ValidateDataAnnotations()
            .Validate(settings => settings.HasValidSigningKey(),
                "Configure Jwt:SigningKey with at least 32 random bytes encoded as Base64.")
            .ValidateOnStart();
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuthenticationService>();
        services.AddScoped<AdministratorBootstrap>();
        services.AddScoped<StaffJwtEvents>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, settings) =>
            {
                var jwt = settings.Value;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.EventsType = typeof(StaffJwtEvents);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)),
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidTypes = ["JWT"],
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = "sub",
                    RoleClaimType = "role"
                };
            });
        services.AddAuthorization(options =>
        {
            foreach (var policy in AccessPolicies.RolesByPermission)
                options.AddPolicy(policy.Key, rule => rule.RequireAuthenticatedUser().RequireRole(policy.Value));
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });
        return services;
    }
}
