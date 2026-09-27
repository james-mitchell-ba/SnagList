namespace SnagList.Api.Tests.Hypermedia;

using SnagList.Api.Hypermedia;
using SnagList.Domain.Snags;
using SnagList.Domain.Staff;
using Xunit;

public class SnagLinksBuilderTests
{
    private static readonly Guid SnagId = Guid.NewGuid();

    [Fact]
    public void Reporter_sees_edit_and_withdraw_while_Reported()
    {
        var links = SnagLinksBuilder.Build(SnagId, SnagStatus.Reported, "U1", "U1", [StaffRole.Staff]);

        Assert.Contains("edit", links.Keys);
        Assert.Contains("withdraw", links.Keys);
        Assert.DoesNotContain("acknowledge", links.Keys);
    }

    [Fact]
    public void A_different_staff_member_does_not_see_edit_or_withdraw()
    {
        var links = SnagLinksBuilder.Build(SnagId, SnagStatus.Reported, "U1", "U2", [StaffRole.Staff]);

        Assert.DoesNotContain("edit", links.Keys);
        Assert.DoesNotContain("withdraw", links.Keys);
    }

    [Fact]
    public void Maintenance_sees_acknowledge_and_reject_while_Reported()
    {
        var links = SnagLinksBuilder.Build(SnagId, SnagStatus.Reported, "U1", "U9", [StaffRole.Maintenance]);

        Assert.Contains("acknowledge", links.Keys);
        Assert.Contains("reject", links.Keys);
        Assert.DoesNotContain("edit", links.Keys);
    }

    [Fact]
    public void Maintenance_sees_only_close_while_Resolved()
    {
        var links = SnagLinksBuilder.Build(SnagId, SnagStatus.Resolved, "U1", "U9", [StaffRole.Maintenance]);

        Assert.Contains("close", links.Keys);
        Assert.DoesNotContain("acknowledge", links.Keys);
        Assert.DoesNotContain("resolve", links.Keys);
    }

    [Fact]
    public void Comments_and_photos_links_are_always_present_regardless_of_status_or_role()
    {
        var links = SnagLinksBuilder.Build(SnagId, SnagStatus.Closed, "U1", "U9", [StaffRole.Staff]);

        Assert.Contains("comments", links.Keys);
        Assert.Contains("photos", links.Keys);
    }

    [Fact]
    public void BuildSummaryLinks_carries_only_self_comments_and_photos()
    {
        var links = SnagLinksBuilder.BuildSummaryLinks(SnagId);

        Assert.Equal(["comments", "photos", "self"], links.Keys.OrderBy(k => k));
    }
}
