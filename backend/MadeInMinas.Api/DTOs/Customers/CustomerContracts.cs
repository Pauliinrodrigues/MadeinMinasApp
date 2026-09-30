using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using MadeInMinas.Api.Validation;

namespace MadeInMinas.Api.DTOs.Customers;

public sealed record CustomerRequest(
    [Required, StringLength(120)] string Name,
    [Required, StringLength(30)] string Phone,
    bool IsActive = true) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!BrazilianPhone.TryNormalize(Phone, out _))
            yield return new ValidationResult("Informe telefone brasileiro com DDD: fixo com oito dígitos ou celular com nove, iniciado em 9.", [nameof(Phone)]);
    }
}

public sealed record CustomerStatusRequest([Required] bool? IsActive);

public sealed class CustomerListQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(120)] public string? Search { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record CustomerResponse(Guid Id, string Name, string Phone, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record CustomerPageResponse(CustomerResponse[] Items, int Page, int PageSize, int TotalCount);

public sealed record AddressRequest(
    [Required, StringLength(120)] string Street,
    [Required, StringLength(20)] string Number,
    [Required, StringLength(80)] string Neighborhood,
    [Required, StringLength(80)] string City,
    [Required, StringLength(2)] string State,
    bool IsActive = true,
    [StringLength(120)] string? Complement = null,
    [StringLength(10)] string? PostalCode = null,
    [StringLength(250)] string? Reference = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (State is not null && !Regex.IsMatch(State.ToUpperInvariant(), "^(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)$"))
            yield return new ValidationResult("Selecione uma UF brasileira válida.", [nameof(State)]);
        if (!string.IsNullOrWhiteSpace(PostalCode) && !Regex.IsMatch(PostalCode.Trim(), "^[0-9]{5}-?[0-9]{3}$"))
            yield return new ValidationResult("Informe CEP com oito dígitos, com ou sem hífen.", [nameof(PostalCode)]);
    }
}

public sealed class AddressListQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public bool? IsActive { get; init; }
}

public sealed record AddressResponse(Guid Id, Guid CustomerId, string Street, string Number,
    string Neighborhood, string City, string State, bool IsActive, string? Complement,
    string? PostalCode, string? Reference, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AddressPageResponse(AddressResponse[] Items, int Page, int PageSize, int TotalCount);
