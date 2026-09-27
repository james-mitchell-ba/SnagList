namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class EditSnagCommandTests
{
    private static Snag ReportedSnag(out FakeSnagRepository repo)
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Plumbing, SnagSeverity.Low,
            "Dripping tap", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        repo = new FakeSnagRepository();
        repo.Add(snag);
        return snag;
    }

    [Fact]
    public async Task Edits_a_Reported_Snag_when_the_reporter_calls_and_version_matches()
    {
        var snag = ReportedSnag(out var repo);
        var handler = new EditSnagCommandHandler(repo, new FakeUnitOfWork());

        await handler.HandleAsync(
            new EditSnagCommand(snag.Id, "U123456", "4th floor", SnagCategory.Electrical, SnagSeverity.High,
                "Actually wiring", snag.Version),
            default);

        Assert.Equal("4th floor", repo.Store[snag.Id].SubLocation);
    }

    [Fact]
    public async Task Throws_when_a_different_staff_member_tries_to_edit()
    {
        var snag = ReportedSnag(out var repo);
        var handler = new EditSnagCommandHandler(repo, new FakeUnitOfWork());

        await Assert.ThrowsAsync<UnauthorizedSnagActionException>(() => handler.HandleAsync(
            new EditSnagCommand(snag.Id, "U999999", "4th floor", SnagCategory.Electrical, SnagSeverity.High,
                "Actually wiring", snag.Version),
            default));
    }

    [Fact]
    public async Task Throws_a_version_conflict_when_expectedVersion_is_stale()
    {
        var snag = ReportedSnag(out var repo);
        var handler = new EditSnagCommandHandler(repo, new FakeUnitOfWork());

        var ex = await Assert.ThrowsAsync<SnagVersionConflictException>(() => handler.HandleAsync(
            new EditSnagCommand(snag.Id, "U123456", "4th floor", SnagCategory.Electrical, SnagSeverity.High,
                "Actually wiring", snag.Version + 1),
            default));

        Assert.Equal(snag.Version, ex.ActualVersion);
    }

    [Fact]
    public async Task Throws_SnagNotEditableException_once_Acknowledged()
    {
        var snag = ReportedSnag(out var repo);
        snag.TransitionTo(SnagStatus.Acknowledged, "U999999", DateTimeOffset.UtcNow);
        var handler = new EditSnagCommandHandler(repo, new FakeUnitOfWork());

        await Assert.ThrowsAsync<SnagNotEditableException>(() => handler.HandleAsync(
            new EditSnagCommand(snag.Id, "U123456", "4th floor", SnagCategory.Electrical, SnagSeverity.High,
                "too late", snag.Version),
            default));
    }
}
