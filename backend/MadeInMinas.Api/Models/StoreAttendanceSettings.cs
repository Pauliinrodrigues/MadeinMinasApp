namespace MadeInMinas.Api.Models;

public sealed class StoreAttendanceSettings
{
    public int Id { get; set; } = 1;
    public long Version { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedById { get; set; }
    public string UpdatedByName { get; set; } = "";
}
