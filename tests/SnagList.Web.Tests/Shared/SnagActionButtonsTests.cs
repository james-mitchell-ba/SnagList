namespace SnagList.Web.Tests.Shared;

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SnagList.Contracts;
using SnagList.Web.Shared;
using Xunit;

public class SnagActionButtonsTests : TestContext
{
    [Fact]
    public void Renders_edit_and_withdraw_when_present_in_Links_and_nothing_else()
    {
        var links = new Dictionary<string, ApiLink>
        {
            ["self"] = new("/x", "GET", "GetSnag"),
            ["edit"] = new("/x", "PATCH", "EditSnag"),
            ["withdraw"] = new("/x/withdraw", "POST", "WithdrawSnag"),
        };

        var component = RenderComponent<SnagActionButtons>(p => p.Add(x => x.Links, links));

        Assert.NotEmpty(component.FindAll("#action-edit"));
        Assert.NotEmpty(component.FindAll("#action-withdraw"));
        Assert.Empty(component.FindAll("#action-acknowledge"));
        Assert.Empty(component.FindAll("#action-resolve"));
        Assert.Empty(component.FindAll("#action-close"));
        Assert.Empty(component.FindAll("#action-reject"));
    }

    [Fact]
    public void Renders_only_close_when_that_is_the_only_transition_link_present()
    {
        var links = new Dictionary<string, ApiLink>
        {
            ["self"] = new("/x", "GET", "GetSnag"),
            ["close"] = new("/x/close", "POST", "CloseSnag"),
        };

        var component = RenderComponent<SnagActionButtons>(p => p.Add(x => x.Links, links));

        Assert.NotEmpty(component.FindAll("#action-close"));
        Assert.Empty(component.FindAll("#action-acknowledge"));
        Assert.Empty(component.FindAll("#action-resolve"));
        Assert.Empty(component.FindAll("#action-edit"));
    }

    [Fact]
    public void Renders_no_transition_buttons_when_Links_carries_only_self_comments_and_photos()
    {
        var links = new Dictionary<string, ApiLink>
        {
            ["self"] = new("/x", "GET", "GetSnag"),
            ["comments"] = new("/x/comments", "POST", "AddSnagComment"),
            ["photos"] = new("/x/photos", "POST", "UploadSnagPhoto"),
        };

        var component = RenderComponent<SnagActionButtons>(p => p.Add(x => x.Links, links));

        Assert.Empty(component.FindAll(".snag-actions button"));
    }

    [Fact]
    public void Clicking_Acknowledge_invokes_OnAcknowledge()
    {
        var invoked = false;
        var links = new Dictionary<string, ApiLink> { ["acknowledge"] = new("/x/acknowledge", "POST", "AcknowledgeSnag") };

        var component = RenderComponent<SnagActionButtons>(p => p
            .Add(x => x.Links, links)
            .Add(x => x.OnAcknowledge, EventCallback.Factory.Create(this, () => invoked = true)));

        component.Find("#action-acknowledge").Click();

        Assert.True(invoked);
    }
}
