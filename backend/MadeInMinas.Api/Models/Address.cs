namespace MadeInMinas.Api.Models;

public sealed class Address
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string Street { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string? Complement { get; set; }
    public string Neighborhood { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public string? Reference { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
