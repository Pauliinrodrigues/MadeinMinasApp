using System.Text.RegularExpressions;

namespace MadeInMinas.Api.Services;

public sealed class PublicDeliveryOptions
{
    public PublicDeliveryArea[] Areas { get; set; } = [];

    public bool IsValid() => Areas is { Length: <= 500 }
        && Areas.All(area => area is not null && area.IsValid())
        && Areas.Select(area => area.Id).Distinct(StringComparer.Ordinal).Count() == Areas.Length
        && Areas.Select(area => $"{area.State}|{area.City.Trim().Normalize()}|{area.Neighborhood.Trim().Normalize()}")
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == Areas.Length;
}

public sealed class PublicDeliveryArea
{
    public string Id { get; set; } = "";
    public string Neighborhood { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public decimal? Fee { get; set; }

    public bool IsValid() => Id is not null && Regex.IsMatch(Id, "^[a-z0-9][a-z0-9-]{0,59}$")
        && !string.IsNullOrWhiteSpace(Neighborhood) && Neighborhood.Length <= 80
        && !string.IsNullOrWhiteSpace(City) && City.Length <= 80
        && State is not null && Regex.IsMatch(State, "^(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)$")
        && Fee is >= 0 and <= 9999.99m && decimal.Round(Fee.Value, 2) == Fee.Value;
}
