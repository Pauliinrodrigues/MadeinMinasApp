namespace MadeInMinas.Api.DTOs;

public sealed record SystemStatusResponse(string Status)
{
    public string Environment { get; init; } = "";
    public string Version { get; init; } = typeof(SystemStatusResponse).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
}
