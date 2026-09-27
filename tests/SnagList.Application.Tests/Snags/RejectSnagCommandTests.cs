namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class RejectSnagCommandTests
{
    [Fact]
    public async Task Rejects_a_Snag_and_records_the_reason_as_a_comment()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Other, SnagSeverity.Low,
            "Reported twice by mistake", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new RejectSnagCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await handler.HandleAsync(
            new RejectSnagCommand(snag.Id, "U999999", "Bob Maintenance", "Duplicate of an earlier report", snag.Version),
            default);

        var stored = repo.Store[snag.Id];
        Assert.Equal(SnagStatus.Rejected, stored.Status);
        var comment = Assert.Single(stored.Comments);
        Assert.Equal("Duplicate of an earlier report", comment.Body);
        Assert.Equal("U999999", comment.AuthorStaffId);
    }
}
