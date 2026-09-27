namespace SnagList.Application.Notifications;

using SnagList.Application.Abstractions;
using SnagList.Domain.Snags.Events;

public sealed class SnagNotificationDispatcher(
    IEmailSender emailSender, IStaffIdentityRepository staffIdentities, NotificationOptions options)
{
    public async Task DispatchAsync(IReadOnlyList<object> domainEvents, CancellationToken ct)
    {
        foreach (var domainEvent in domainEvents)
        {
            switch (domainEvent)
            {
                case SnagReported reported:
                    await emailSender.SendAsync(
                        options.MaintenanceTeamEmail,
                        "New Snag reported",
                        $"A new {reported.Severity} severity Snag was reported (id: {reported.SnagId}).",
                        ct);
                    break;

                case SnagStatusChanged changed:
                    var reporter = await staffIdentities.GetAsync(changed.ReportedByStaffId, ct);
                    if (reporter is not null)
                    {
                        await emailSender.SendAsync(
                            reporter.Email,
                            $"Your Snag report changed status: {changed.NewStatus}",
                            $"Snag {changed.SnagId} moved from {changed.PreviousStatus} to {changed.NewStatus}.",
                            ct);
                    }
                    break;
            }
        }
    }
}
