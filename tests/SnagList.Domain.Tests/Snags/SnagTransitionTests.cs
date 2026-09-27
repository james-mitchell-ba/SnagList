namespace SnagList.Domain.Tests.Snags;

using SnagList.Domain.Snags;
using Xunit;

public class SnagTransitionTests
{
    private static Snag NewSnag() => Snag.Report(
        Guid.NewGuid(), "3rd floor, room 3.12", SnagCategory.Electrical, SnagSeverity.Medium,
        "Flickering light", "U123456", "Jane Smith", DateTimeOffset.UtcNow);

    // Walks the aggregate forward one legal step at a time until it reaches `target`,
    // so each test only has to assert the one edge it's actually checking.
    private static void DriveToStatus(Snag snag, SnagStatus target)
    {
        if (target == SnagStatus.Withdrawn)
        {
            snag.TransitionTo(SnagStatus.Withdrawn, "U000000", DateTimeOffset.UtcNow);
            return;
        }

        if (target == SnagStatus.Rejected)
        {
            snag.TransitionTo(SnagStatus.Rejected, "U000000", DateTimeOffset.UtcNow);
            return;
        }

        while (snag.Status != target)
        {
            var next = snag.Status switch
            {
                SnagStatus.Reported => SnagStatus.Acknowledged,
                SnagStatus.Acknowledged => SnagStatus.InProgress,
                SnagStatus.InProgress => SnagStatus.Resolved,
                SnagStatus.Resolved => SnagStatus.Closed,
                _ => throw new InvalidOperationException($"cannot drive from {snag.Status} toward {target}"),
            };
            snag.TransitionTo(next, "U000000", DateTimeOffset.UtcNow);
        }
    }

    [Theory]
    [InlineData(SnagStatus.Reported, SnagStatus.Acknowledged)]
    [InlineData(SnagStatus.Reported, SnagStatus.Rejected)]
    [InlineData(SnagStatus.Reported, SnagStatus.Withdrawn)]
    [InlineData(SnagStatus.Acknowledged, SnagStatus.InProgress)]
    [InlineData(SnagStatus.Acknowledged, SnagStatus.Rejected)]
    [InlineData(SnagStatus.InProgress, SnagStatus.Resolved)]
    [InlineData(SnagStatus.Resolved, SnagStatus.Closed)]
    public void Allows_every_legal_transition(SnagStatus from, SnagStatus to)
    {
        var snag = NewSnag();
        DriveToStatus(snag, from);

        snag.TransitionTo(to, "U000000", DateTimeOffset.UtcNow);

        Assert.Equal(to, snag.Status);
    }

    [Theory]
    [InlineData(SnagStatus.Reported, SnagStatus.InProgress)]
    [InlineData(SnagStatus.Reported, SnagStatus.Resolved)]
    [InlineData(SnagStatus.Reported, SnagStatus.Closed)]
    [InlineData(SnagStatus.Acknowledged, SnagStatus.Withdrawn)]
    [InlineData(SnagStatus.Acknowledged, SnagStatus.Reported)]
    [InlineData(SnagStatus.Acknowledged, SnagStatus.Resolved)]
    [InlineData(SnagStatus.InProgress, SnagStatus.Rejected)]
    [InlineData(SnagStatus.InProgress, SnagStatus.Acknowledged)]
    [InlineData(SnagStatus.InProgress, SnagStatus.Withdrawn)]
    [InlineData(SnagStatus.Resolved, SnagStatus.InProgress)]
    [InlineData(SnagStatus.Resolved, SnagStatus.Rejected)]
    [InlineData(SnagStatus.Closed, SnagStatus.Reported)]
    [InlineData(SnagStatus.Closed, SnagStatus.InProgress)]
    [InlineData(SnagStatus.Rejected, SnagStatus.Reported)]
    [InlineData(SnagStatus.Rejected, SnagStatus.Acknowledged)]
    [InlineData(SnagStatus.Withdrawn, SnagStatus.Reported)]
    public void Rejects_every_illegal_transition(SnagStatus from, SnagStatus to)
    {
        var snag = NewSnag();
        DriveToStatus(snag, from);

        var ex = Assert.Throws<InvalidSnagStatusTransitionException>(
            () => snag.TransitionTo(to, "U000000", DateTimeOffset.UtcNow));

        Assert.Equal(from, ex.From);
        Assert.Equal(to, ex.To);
    }

    [Fact]
    public void TransitionTo_raises_SnagStatusChanged_with_previous_and_new_status()
    {
        var snag = NewSnag();

        snag.TransitionTo(SnagStatus.Acknowledged, "U999999", DateTimeOffset.UtcNow);

        var changed = Assert.Single(snag.DomainEvents.OfType<SnagList.Domain.Snags.Events.SnagStatusChanged>());
        Assert.Equal(SnagStatus.Reported, changed.PreviousStatus);
        Assert.Equal(SnagStatus.Acknowledged, changed.NewStatus);
        Assert.Equal("U999999", changed.ChangedByStaffId);
    }

    [Fact]
    public void Report_raises_SnagReported()
    {
        var snag = NewSnag();

        var evt = Assert.Single(snag.DomainEvents);
        var reported = Assert.IsType<SnagList.Domain.Snags.Events.SnagReported>(evt);
        Assert.Equal(snag.Id, reported.SnagId);
        Assert.Equal(SnagSeverity.Medium, reported.Severity);
    }
}
