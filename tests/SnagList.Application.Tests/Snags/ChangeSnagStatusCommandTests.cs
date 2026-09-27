namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class ChangeSnagStatusCommandTests
{
    private static Snag NewSnag() => Snag.Report(
        Guid.NewGuid(), "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
        "Flickering light", "U123456", "Jane Smith", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Transitions_to_the_target_status_when_the_edge_is_legal()
    {
        var snag = NewSnag();
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new ChangeSnagStatusCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await handler.HandleAsync(
            new ChangeSnagStatusCommand(snag.Id, SnagStatus.Acknowledged, "U999999", snag.Version), default);

        Assert.Equal(SnagStatus.Acknowledged, repo.Store[snag.Id].Status);
    }

    [Fact]
    public async Task Throws_InvalidSnagStatusTransitionException_for_an_illegal_edge()
    {
        var snag = NewSnag();
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new ChangeSnagStatusCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidSnagStatusTransitionException>(() => handler.HandleAsync(
            new ChangeSnagStatusCommand(snag.Id, SnagStatus.Resolved, "U999999", snag.Version), default));
    }

    [Fact]
    public async Task Throws_a_version_conflict_when_expectedVersion_is_stale()
    {
        var snag = NewSnag();
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new ChangeSnagStatusCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<SnagVersionConflictException>(() => handler.HandleAsync(
            new ChangeSnagStatusCommand(snag.Id, SnagStatus.Acknowledged, "U999999", snag.Version + 1), default));
    }
}
