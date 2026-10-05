using System.Security.Cryptography;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Chat;
using Microsoft.AspNetCore.DataProtection;

namespace MadeInMinas.Api.Security;

public sealed class PublicChatAccess(IDataProtectionProvider provider, TimeProvider clock)
{
    private readonly IDataProtector protector = provider.CreateProtector("MadeInMinas.HumanChat.v1");

    public ChatAccessResponse? Issue(Guid conversationId, DateTimeOffset createdAt)
    {
        var expiresAt = createdAt.AddDays(7);
        return expiresAt <= clock.GetUtcNow() ? null
            : new ChatAccessResponse(protector.Protect(JsonSerializer.Serialize(new Payload(conversationId, expiresAt))), expiresAt);
    }

    public Guid? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
            return null;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(token));
            return payload is not null && payload.Id != Guid.Empty && payload.ExpiresAt > clock.GetUtcNow() ? payload.Id : null;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    private sealed record Payload(Guid Id, DateTimeOffset ExpiresAt);
}
