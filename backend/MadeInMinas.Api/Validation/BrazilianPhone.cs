using System.Text.RegularExpressions;

namespace MadeInMinas.Api.Validation;

internal static partial class BrazilianPhone
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 30)
            return false;
        var text = value.Trim();
        if (!AllowedCharacters().IsMatch(text))
            return false;
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is 12 or 13 && digits.StartsWith("55", StringComparison.Ordinal))
            digits = digits[2..];
        else if (text.StartsWith('+'))
            return false;
        if (!NationalNumber().IsMatch(digits))
            return false;
        normalized = "+55" + digits;
        return true;
    }

    public static string? SearchDigits(string value) => AllowedCharacters().IsMatch(value.Trim())
        ? new string(value.Where(char.IsAsciiDigit).ToArray()) : null;

    [GeneratedRegex(@"^\+?[0-9 ().\-]+$")]
    private static partial Regex AllowedCharacters();

    [GeneratedRegex(@"^[1-9][0-9](?:[2-5][0-9]{7}|9[0-9]{8})$")]
    private static partial Regex NationalNumber();
}
