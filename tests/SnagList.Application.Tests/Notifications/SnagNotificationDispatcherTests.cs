namespace SnagList.Application.Tests.Notifications;

using SnagList.Application.Notifications;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using SnagList.Domain.Snags.Events;
using SnagList.Domain.Staff;
using Xunit;

public class SnagNotificationDispatcherTests
{
    private static SnagNotificationDispatcher Build(FakeEmailSender emailSender, FakeStaffIdentityRepository staffIdentities) =>
        new(emailSender, staffIdentities, new NotificationOptions { MaintenanceTeamEmail = "maintenance@example.com" });

    [Fact]
    public async Task SnagReported_emails_the_maintenance_team_address()
    {
        var emailSender = new FakeEmailSender();
        var dispatcher = Build(emailSender, new FakeStaffIdentityRepository());

        await dispatcher.DispatchAsync(
            [new SnagReported(Guid.NewGuid(), Guid.NewGuid(), SnagSeverity.SafetyCritical, "U1", DateTimeOffset.UtcNow)], default);

        var sent = Assert.Single(emailSender.SentEmails);
        Assert.Equal("maintenance@example.com", sent.To);
    }

    [Fact]
    public async Task SnagStatusChanged_emails_the_reporter_when_their_identity_is_known()
    {
        var emailSender = new FakeEmailSender();
        var staffIdentities = new FakeStaffIdentityRepository();
        staffIdentities.Add(StaffIdentity.FirstSeen("U1", "Jane Smith", "jane@example.com", [StaffRole.Staff], DateTimeOffset.UtcNow));
        var dispatcher = Build(emailSender, staffIdentities);

        await dispatcher.DispatchAsync(
            [new SnagStatusChanged(Guid.NewGuid(), SnagStatus.Reported, SnagStatus.Acknowledged, "U9", "U1", DateTimeOffset.UtcNow)],
            default);

        var sent = Assert.Single(emailSender.SentEmails);
        Assert.Equal("jane@example.com", sent.To);
    }

    [Fact]
    public async Task SnagStatusChanged_sends_nothing_when_the_reporter_is_not_yet_mirrored()
    {
        var emailSender = new FakeEmailSender();
        var dispatcher = Build(emailSender, new FakeStaffIdentityRepository());

        await dispatcher.DispatchAsync(
            [new SnagStatusChanged(Guid.NewGuid(), SnagStatus.Reported, SnagStatus.Acknowledged, "U9", "U1", DateTimeOffset.UtcNow)],
            default);

        Assert.Empty(emailSender.SentEmails);
    }
}
