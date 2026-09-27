namespace SnagList.Application.Tests.Staff;

using SnagList.Application.Staff.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Staff;
using Xunit;

public class SyncStaffIdentityCommandTests
{
    [Fact]
    public async Task First_sight_creates_a_new_StaffIdentity()
    {
        var repo = new FakeStaffIdentityRepository();
        var handler = new SyncStaffIdentityCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await handler.HandleAsync(new SyncStaffIdentityCommand("U123456", "Jane Smith", "jane@example.com", [StaffRole.Staff]), default);

        Assert.True(repo.Store.ContainsKey("U123456"));
    }

    [Fact]
    public async Task A_second_sight_syncs_the_existing_row_instead_of_duplicating()
    {
        var repo = new FakeStaffIdentityRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var handler = new SyncStaffIdentityCommandHandler(repo, new FakeUnitOfWork(), clock);
        await handler.HandleAsync(new SyncStaffIdentityCommand("U123456", "Jane Smith", "jane@example.com", [StaffRole.Staff]), default);

        clock.UtcNow = clock.UtcNow.AddDays(1);
        await handler.HandleAsync(
            new SyncStaffIdentityCommand("U123456", "Jane Smith", "jane@example.com", [StaffRole.Staff, StaffRole.Maintenance]),
            default);

        Assert.Single(repo.Store);
        Assert.Equal([StaffRole.Staff, StaffRole.Maintenance], repo.Store["U123456"].Roles);
    }
}
