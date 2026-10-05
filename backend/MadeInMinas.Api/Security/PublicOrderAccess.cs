using System.Security.Cryptography;
using System.Text.Json;
using MadeInMinas.Api.DTOs.PublicOrders;
using Microsoft.AspNetCore.DataProtection;

namespace MadeInMinas.Api.Security;

public sealed class PublicOrderAccess(IDataProtectionProvider provider, TimeProvider clock)
{
    private readonly IDataProtector protector = provider.CreateProtector("MadeInMinas.PublicOrderTracking.v1");

    public PublicOrderAccessResponse? Issue(Guid orderId, DateTimeOffset createdAt)
    {
        var expiresAt = createdAt.AddDays(7);
        if (orderId == Guid.Empty || expiresAt <= clock.GetUtcNow())
            return null;
        // Repetir o checkout recupera o acesso ao mesmo pedido, sem renovar o prazo.
        var token = protector.Protect(JsonSerializer.Serialize(new AccessPayload(orderId, expiresAt)));
        return new PublicOrderAccessResponse(token, expiresAt);
    }

    public Guid? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
            return null;
        try
        {
            var payload = JsonSerializer.Deserialize<AccessPayload>(protector.Unprotect(token));
            return payload is not null && payload.OrderId != Guid.Empty && payload.ExpiresAt > clock.GetUtcNow()
                ? payload.OrderId : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record AccessPayload(Guid OrderId, DateTimeOffset ExpiresAt);
}
