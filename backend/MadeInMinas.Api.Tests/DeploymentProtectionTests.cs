using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MadeInMinas.Api.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MadeInMinas.Api.Tests;

public sealed class DeploymentProtectionTests
{
    private sealed class Host : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "MadeInMinas.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private static ServiceProvider Provider(Dictionary<string, string?> settings, string environment = "Production")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDeploymentProtection(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new Host { EnvironmentName = environment });
        return services.BuildServiceProvider();
    }
    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void DeploymentRejectsWrongDatabaseBeforeOpeningConnection(string environment)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Deployment:DatabaseName"] = "made_in_minas_" + environment.ToLowerInvariant(),
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=made_in_minas"
        };
        var host = new Host { EnvironmentName = environment };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        Assert.Throws<InvalidOperationException>(() => DeploymentRegistration.ValidateDatabaseEnvironment(configuration, host));
        configuration["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=" + settings["Deployment:DatabaseName"];
        DeploymentRegistration.ValidateDatabaseEnvironment(configuration, host);
        configuration["Deployment:DatabaseName"] = null;
        Assert.Throws<InvalidOperationException>(() => DeploymentRegistration.ValidateDatabaseEnvironment(configuration, host));
    }
    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void InvalidDeploymentConnectionDoesNotExposeCredentials(string environment)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Deployment:DatabaseName"] = "expected",
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=expected;Password=fictitious-sensitive-value;UnsupportedKeyword=true"
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => DeploymentRegistration.ValidateDatabaseEnvironment(configuration, new Host { EnvironmentName = environment }));
        Assert.DoesNotContain("fictitious-sensitive-value", error.ToString());
        Assert.Null(error.InnerException);
    }
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void LocalAndAutomatedTestsKeepTheirExistingDatabaseConfiguration(string environment)
    {
        DeploymentRegistration.ValidateDatabaseEnvironment(new ConfigurationBuilder().Build(), new Host { EnvironmentName = environment });
        using var provider = Provider([], environment);
        Assert.Equal("MadeInMinas.Api", provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }
    [Fact]
    public void ProductionRequiresExplicitPersistentKeyLocation()
    {
        Assert.Throws<InvalidOperationException>(() => Provider([]));
        Assert.Throws<InvalidOperationException>(() => Provider(new() { ["DataProtection:KeyRingPath"] = "relative-directory" }));
        using var development = Provider([], "Development");
    }
    [Fact]
    public void ProxyTrustIsRestrictedToConfiguredIndividualAddresses()
    {
        using var provider = Provider(new() { ["ReverseProxy:KnownProxies:0"] = "192.0.2.10" }, "Development");
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal([IPAddress.Parse("192.0.2.10")], options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Throws<InvalidOperationException>(() => Provider(new() { ["ReverseProxy:KnownProxies:0"] = "0.0.0.0" }, "Development"));
    }
    [Fact]
    public void EncryptedKeyRingPreservesVisitorAccessAcrossServiceRestarts()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "protection-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=MadeInMinas-Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var certificatePath = Path.Combine(root, "test.pfx");
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
            var settings = new Dictionary<string, string?>
            {
                ["DataProtection:KeyRingPath"] = Path.Combine(root, "keys"),
                ["DataProtection:CertificateFile"] = certificatePath
            };
            string token;
            using (var first = Provider(settings))
                token = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("visitor-test").Protect("test-capability");
            using var second = Provider(settings);
            Assert.Equal("test-capability", second.GetRequiredService<IDataProtectionProvider>().CreateProtector("visitor-test").Unprotect(token));
            using var staging = Provider(settings, "Staging");
            var stagingProtector = staging.GetRequiredService<IDataProtectionProvider>().CreateProtector("visitor-test");
            Assert.Throws<CryptographicException>(() => stagingProtector.Unprotect(token));
            var stagingToken = stagingProtector.Protect("staging-capability");
            Assert.Throws<CryptographicException>(() => second.GetRequiredService<IDataProtectionProvider>().CreateProtector("visitor-test").Unprotect(stagingToken));
            using var stagingRestarted = Provider(settings, "Staging");
            Assert.Equal("staging-capability", stagingRestarted.GetRequiredService<IDataProtectionProvider>().CreateProtector("visitor-test").Unprotect(stagingToken));
            Assert.Contains("encryptedSecret", File.ReadAllText(Directory.GetFiles(settings["DataProtection:KeyRingPath"]!, "*.xml").Single()));
        }
        finally
        {
            var isolatedRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "protection-tests")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(root).StartsWith(isolatedRoot, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, recursive: true);
        }
    }
}
