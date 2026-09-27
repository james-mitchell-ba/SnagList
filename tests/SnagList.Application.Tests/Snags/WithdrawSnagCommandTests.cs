namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class WithdrawSnagCommandTests
{
    [Fact]
    public async Task Withdraws_a_Reported_Snag_when_the_reporter_calls()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Other, SnagSeverity.Low,
            "Squeaky door", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var handler = new WithdrawSnagCommandHandler(repo, new FakeUnitOfWork(), clock);

        await handler.HandleAsync(new WithdrawSnagCommand(snag.Id, "U123456", snag.Version), default);

        Assert.Equal(SnagStatus.Withdrawn, repo.Store[snag.Id].Status);
    }

    [Fact]
    public async Task Throws_when_a_different_staff_member_tries_to_withdraw()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Other, SnagSeverity.Low,
            "Squeaky door", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new WithdrawSnagCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<UnauthorizedSnagActionException>(
            () => handler.HandleAsync(new WithdrawSnagCommand(snag.Id, "U999999", snag.Version), default));
    }
}
