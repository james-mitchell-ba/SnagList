namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class AddSnagCommentCommandTests
{
    [Fact]
    public async Task Adds_a_comment_regardless_of_status()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Other, SnagSeverity.Low,
            "Squeaky door", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        snag.TransitionTo(SnagStatus.Acknowledged, "U999999", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new AddSnagCommentCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await handler.HandleAsync(
            new AddSnagCommentCommand(snag.Id, "U999999", "Bob Maintenance", "Parts ordered"), default);

        Assert.Equal("Parts ordered", Assert.Single(repo.Store[snag.Id].Comments).Body);
    }
}
