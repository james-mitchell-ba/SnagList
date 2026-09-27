namespace SnagList.Domain.Tests.Snags;

using SnagList.Domain.Snags;
using Xunit;

public class SnagCommentTests
{
    [Fact]
    public void AddComment_appends_a_comment_regardless_of_status()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Other, SnagSeverity.Low,
            "Squeaky door", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        snag.TransitionTo(SnagStatus.Acknowledged, "U999999", DateTimeOffset.UtcNow);

        snag.AddComment("U999999", "Bob Maintenance", "Parts ordered, ETA Friday", DateTimeOffset.UtcNow);

        var comment = Assert.Single(snag.Comments);
        Assert.Equal("U999999", comment.AuthorStaffId);
        Assert.Equal("Parts ordered, ETA Friday", comment.Body);
        Assert.Equal(snag.Id, comment.SnagId);
    }
}
