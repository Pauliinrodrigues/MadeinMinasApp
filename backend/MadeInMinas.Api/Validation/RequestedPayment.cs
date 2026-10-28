using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.Validation;

public static class RequestedPayment
{
    public static IEnumerable<ValidationResult> Validate(string? method, decimal? changeFor)
    {
        if (method is not null && method is not ("Card" or "Cash" or "Pix"))
            yield return new ValidationResult("Escolha cartão, dinheiro ou Pix.", ["PaymentPreference"]);
        if (changeFor is { } amount && (amount <= 0 || amount > 999999.99m || decimal.Round(amount, 2) != amount))
            yield return new ValidationResult("Informe um valor válido para o troco, com até duas casas decimais.", ["ChangeFor"]);
        if (changeFor is not null && method is not null && method != "Cash")
            yield return new ValidationResult("Troco só pode ser solicitado para dinheiro.", ["ChangeFor"]);
    }
    public static string Label(string? method) => method switch
    {
        "Card" => "Cartão",
        "Cash" => "Dinheiro",
        "Pix" => "Pix",
        _ => "Não informado"
    };
}
