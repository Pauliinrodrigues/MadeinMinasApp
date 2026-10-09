using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MadeInMinas.Api.Services;

namespace MadeInMinas.Api.DTOs.Delivery;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryAreaRequest(
    [Required, RegularExpression("^[a-z0-9][a-z0-9-]{0,59}$")] string Id,
    [Required, StringLength(80)] string Neighborhood,
    [Required, StringLength(80)] string City,
    [Required, StringLength(2)] string State,
    [Required, Range(typeof(decimal), "0", "9999.99", ParseLimitsInInvariantCulture = true)] decimal? Fee,
    [Required] bool? IsActive)
{
    public bool CoversAllNeighborhoods { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveDeliverySettingsRequest(
    [Required, RegularExpression("^[A-F0-9]{64}$")] string ExpectedRevision,
    [Required, MaxLength(500)] DeliveryAreaRequest[] Areas) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Areas is null || Areas.Any(area => area is null))
        {
            yield return new ValidationResult("Informe uma lista de regiões válida.", [nameof(Areas)]);
            yield break;
        }
        var options = new PublicDeliveryOptions
        {
            Areas = Areas.Select(area => new PublicDeliveryArea
            {
                Id = area.Id,
                Neighborhood = area.Neighborhood,
                City = area.City,
                State = area.State,
                Fee = area.Fee,
                CoversAllNeighborhoods = area.CoversAllNeighborhoods
            }).ToArray()
        };
        if (!options.IsValid())
            yield return new ValidationResult("Use regiões únicas, UF válida e taxa explícita com até duas casas decimais.", [nameof(Areas)]);
    }
}

public sealed record DeliveryAreaResponse(string Id, string Neighborhood, string City, string State, decimal Fee, bool IsActive)
{
    public bool CoversAllNeighborhoods { get; init; }
}
public sealed record DeliverySettingsResponse(DeliveryAreaResponse[] Areas, string Revision, DateTimeOffset? UpdatedAt, string? UpdatedBy)
{
    public decimal? FixedFee { get; init; }
}
