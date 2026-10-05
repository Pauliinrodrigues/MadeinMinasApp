using System.Text.Json;
using MadeInMinas.Api.Security;
using Microsoft.AspNetCore.DataProtection;

namespace MadeInMinas.Api.Tests;

public sealed class PublicOrderAccessTests
{
    private readonly IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
    private readonly TestClock clock = new();

    [Fact]
    public void AccessHasFixedLifetimeAndCannotBeChangedOrUsedForAnotherPurpose()
    {
        var access = new PublicOrderAccess(provider, clock);
        var id = Guid.NewGuid();
        var createdAt = clock.Now;
        var first = access.Issue(id, createdAt)!;
        Assert.Equal(createdAt.AddDays(7), first.ExpiresAt);
        Assert.Equal(id, access.Read(first.Token));
        var bytes = first.Token.ToCharArray();
        bytes[bytes.Length / 2] = bytes[bytes.Length / 2] == 'A' ? 'B' : 'A';
        Assert.Null(access.Read(new string(bytes)));
        var otherPurpose = provider.CreateProtector("MadeInMinas.Other.v1").Protect(
            JsonSerializer.Serialize(new { OrderId = id, ExpiresAt = first.ExpiresAt }));
        Assert.Null(access.Read(otherPurpose));
        clock.Now = createdAt.AddDays(6);
        var retry = access.Issue(id, createdAt)!;
        Assert.Equal(first.ExpiresAt, retry.ExpiresAt);
        Assert.Equal(id, access.Read(retry.Token));
        clock.Now = first.ExpiresAt;
        Assert.Null(access.Read(first.Token));
        Assert.Null(access.Read(retry.Token));
        Assert.Null(access.Issue(id, createdAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1542")]
    [InlineData("+5531999991234")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.payload.signature")]
    public void UnrelatedCredentialsAreRejected(string? token) =>
        Assert.Null(new PublicOrderAccess(provider, clock).Read(token));

    [Fact]
    public void MissingAndMalformedPayloadsAreRejected()
    {
        var access = new PublicOrderAccess(provider, clock);
        var protector = provider.CreateProtector("MadeInMinas.PublicOrderTracking.v1");
        Assert.Null(access.Read(new string('A', 2049)));
        Assert.Null(access.Read(protector.Protect("not-json")));
        Assert.Null(access.Read(protector.Protect("null")));
        Assert.Null(access.Read(protector.Protect("{}")));
        Assert.Null(access.Issue(Guid.Empty, clock.Now));
    }

    [Fact]
    public void PersistedKeysAllowANewProviderToReadExistingAccess()
    {
        var artifactsRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var keysPath = Path.GetFullPath(Path.Combine(artifactsRoot, "tracking-test-keys-" + Guid.NewGuid().ToString("N")));
        Assert.StartsWith(artifactsRoot, keysPath);
        Directory.CreateDirectory(keysPath);
        try
        {
            var firstProvider = DataProtectionProvider.Create(new DirectoryInfo(keysPath), options => options.SetApplicationName("MadeInMinas.Tests"));
            var id = Guid.NewGuid();
            var access = new PublicOrderAccess(firstProvider, clock).Issue(id, clock.Now)!;
            var newProvider = DataProtectionProvider.Create(new DirectoryInfo(keysPath), options => options.SetApplicationName("MadeInMinas.Tests"));
            Assert.Equal(id, new PublicOrderAccess(newProvider, clock).Read(access.Token));
            var otherApplication = DataProtectionProvider.Create(new DirectoryInfo(keysPath), options => options.SetApplicationName("Other.Application"));
            Assert.Null(new PublicOrderAccess(otherApplication, clock).Read(access.Token));
        }
        finally
        {
            Directory.Delete(keysPath, recursive: true);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
