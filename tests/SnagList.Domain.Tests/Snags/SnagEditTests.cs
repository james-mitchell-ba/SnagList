namespace SnagList.Domain.Tests.Snags;

using SnagList.Domain.Snags;
using Xunit;

public class SnagEditTests
{
    private static Snag NewSnag() => Snag.Report(
        Guid.NewGuid(), "3rd floor", SnagCategory.Plumbing, SnagSeverity.Low,
        "Dripping tap", "U123456", "Jane Smith", DateTimeOffset.UtcNow);

    [Fact]
    public void Edit_succeeds_while_Reported()
    {
        var snag = NewSnag();

        snag.Edit("4th floor", SnagCategory.Electrical, SnagSeverity.High, "Actually a wiring issue");

        Assert.Equal("4th floor", snag.SubLocation);
        Assert.Equal(SnagCategory.Electrical, snag.Category);
        Assert.Equal(SnagSeverity.High, snag.Severity);
    }

    [Fact]
    public void Edit_throws_once_Acknowledged()
    {
        var snag = NewSnag();
        snag.TransitionTo(SnagStatus.Acknowledged, "U000000", DateTimeOffset.UtcNow);

        Assert.Throws<SnagNotEditableException>(
            () => snag.Edit("4th floor", SnagCategory.Electrical, SnagSeverity.High, "too late"));
    }

    [Fact]
    public void AddPhoto_throws_on_the_sixth_photo()
    {
        var snag = NewSnag();
        for (var i = 0; i < 5; i++)
        {
            snag.AddPhoto(new SnagPhoto($"blob-{i}", $"photo{i}.jpg", "image/jpeg", 1024, DateTimeOffset.UtcNow));
        }

        Assert.Throws<SnagPhotoLimitExceededException>(
            () => snag.AddPhoto(new SnagPhoto("blob-6", "photo6.jpg", "image/jpeg", 1024, DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void Edit_and_AddPhoto_both_increment_Version()
    {
        var snag = NewSnag();
        var versionAfterReport = snag.Version;

        snag.Edit("4th floor", SnagCategory.Electrical, SnagSeverity.High, "updated");
        Assert.Equal(versionAfterReport + 1, snag.Version);

        snag.AddPhoto(new SnagPhoto("blob-0", "photo0.jpg", "image/jpeg", 1024, DateTimeOffset.UtcNow));
        Assert.Equal(versionAfterReport + 2, snag.Version);
    }
}
