using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.Security;

public sealed class JwtSettings
{
    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";
    [Required] public string SigningKey { get; set; } = "";
    [Range(1, 60)] public int AccessTokenMinutes { get; set; } = 15;

    public bool HasValidSigningKey()
    {
        if (string.IsNullOrWhiteSpace(SigningKey)) return false;
        try { return Convert.FromBase64String(SigningKey).Length >= 32; }
        catch (FormatException) { return false; }
    }
}
