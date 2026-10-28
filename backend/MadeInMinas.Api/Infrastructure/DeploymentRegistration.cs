using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

namespace MadeInMinas.Api.Infrastructure;

public static class DeploymentRegistration
{
    public static void ValidateDatabaseEnvironment(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsStaging() && !environment.IsProduction())
            return;
        var expectedDatabase = configuration["Deployment:DatabaseName"];
        if (string.IsNullOrWhiteSpace(expectedDatabase))
            throw new InvalidOperationException("Configure Deployment:DatabaseName para identificar o banco deste ambiente.");
        NpgsqlConnectionStringBuilder connection;
        try
        {
            connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        }
        catch (ArgumentException)
        {
            // Never include the connection string or its exception in this startup error.
            throw new InvalidOperationException("Configure uma conexao PostgreSQL valida para este ambiente.");
        }
        if (!string.Equals(expectedDatabase, connection.Database, StringComparison.Ordinal))
            throw new InvalidOperationException("O banco da conexao nao corresponde a Deployment:DatabaseName. Confira o ambiente antes de iniciar.");
    }

    public static IServiceCollection AddDeploymentProtection(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        var applicationName = environment.IsDevelopment() || environment.IsEnvironment("Testing")
            ? "MadeInMinas.Api"
            : "MadeInMinas.Api." + environment.EnvironmentName;
        var protection = services.AddDataProtection().SetApplicationName(applicationName);
        var keyPath = configuration["DataProtection:KeyRingPath"];
        var requirePersistence = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            if (requirePersistence)
                throw new InvalidOperationException("Configure DataProtection:KeyRingPath em um volume persistente protegido antes de iniciar este ambiente.");
        }
        else
        {
            if (!Path.IsPathFullyQualified(keyPath))
                throw new InvalidOperationException("DataProtection:KeyRingPath deve ser absoluto.");
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
            var certificateFile = configuration["DataProtection:CertificateFile"];
            if (!string.IsNullOrWhiteSpace(certificateFile))
            {
                if (!Path.IsPathFullyQualified(certificateFile))
                    throw new InvalidOperationException("DataProtection:CertificateFile deve ser absoluto.");
                var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificateFile,
                    configuration["DataProtection:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
                protection.ProtectKeysWithCertificate(certificate);
                // Resolve through options so DI owns the certificate until shutdown.
                services.AddSingleton(_ => certificate);
                services.AddOptions<KeyManagementOptions>().Configure<X509Certificate2>((_, configuredCertificate) =>
                {
                    if (!configuredCertificate.HasPrivateKey)
                        throw new InvalidOperationException("O certificado de protecao deve conter a chave privada.");
                });
            }
            else if (OperatingSystem.IsWindows())
                protection.ProtectKeysWithDpapi();
            else if (requirePersistence)
                throw new InvalidOperationException("Configure um certificado para proteger as chaves persistidas neste ambiente.");
        }
        var proxyAddresses = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        var proxies = proxyAddresses.Select(address => IPAddress.TryParse(address, out var parsed)
            && !parsed.Equals(IPAddress.Any) && !parsed.Equals(IPAddress.IPv6Any) ? parsed
            : throw new InvalidOperationException("Configure apenas enderecos IP individuais de proxies confiaveis.")).ToArray();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            if (proxies.Length > 0)
            {
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var proxy in proxies)
                    options.KnownProxies.Add(proxy);
            }
        });
        return services;
    }
}
