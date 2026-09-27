namespace SnagList.Domain.Tests.Staff;

using SnagList.Domain.Staff;
using Xunit;

public class StaffIdentityTests
{
    [Fact]
    public void FirstSeen_sets_FirstSeenAt_and_LastSeenAt_to_the_same_instant()
    {
        var now = DateTimeOffset.UtcNow;

        var identity = StaffIdentity.FirstSeen("U123456", "Jane Smith", "jane@example.com", [StaffRole.Staff], now);

        Assert.Equal("U123456", identity.StaffId);
        Assert.Equal(now, identity.FirstSeenAt);
        Assert.Equal(now, identity.LastSeenAt);
        Assert.Equal([StaffRole.Staff], identity.Roles);
    }

    [Fact]
    public void Sync_updates_roles_and_LastSeenAt_but_not_FirstSeenAt()
    {
        var firstSeen = DateTimeOffset.UtcNow;
        var identity = StaffIdentity.FirstSeen("U123456", "Jane Smith", "jane@example.com", [StaffRole.Staff], firstSeen);
        var syncedAt = firstSeen.AddDays(1);

        identity.Sync("Jane Smith", "jane@example.com", [StaffRole.Staff, StaffRole.Maintenance], syncedAt);

        Assert.Equal([StaffRole.Staff, StaffRole.Maintenance], identity.Roles);
        Assert.Equal(syncedAt, identity.LastSeenAt);
        Assert.Equal(firstSeen, identity.FirstSeenAt);
    }
}
