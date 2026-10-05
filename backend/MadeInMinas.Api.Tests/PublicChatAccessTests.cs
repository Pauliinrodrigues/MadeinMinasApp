using MadeInMinas.Api.Security;
using Microsoft.AspNetCore.DataProtection;

namespace MadeInMinas.Api.Tests;

public sealed class PublicChatAccessTests
{
    [Fact]
    public void ConversationAccessHasFixedLifetimeAndCannotReadOrderTracking()
    {
        var provider = new EphemeralDataProtectionProvider();
        var clock = new TestClock();
        var access = new PublicChatAccess(provider, clock);
        var id = Guid.NewGuid();
        var createdAt = clock.Now;
        var original = access.Issue(id, createdAt)!;
        Assert.Equal(id, access.Read(original.Token));
        Assert.Equal(createdAt.AddDays(7), original.ExpiresAt);
        Assert.Null(new PublicOrderAccess(provider, clock).Read(original.Token));
        clock.Now = createdAt.AddDays(6);
        var replay = access.Issue(id, createdAt)!;
        Assert.Equal(original.ExpiresAt, replay.ExpiresAt);
        clock.Now = original.ExpiresAt;
        Assert.Null(access.Read(original.Token));
        Assert.Null(access.Read(replay.Token));
        Assert.Null(access.Issue(id, createdAt));
    }
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
