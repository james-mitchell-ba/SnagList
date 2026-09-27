namespace SnagList.Web.Tests.Pages;

using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SnagList.Contracts;
using SnagList.Contracts.Snags;
using SnagList.Web.Pages;
using SnagList.Web.Services;
using SnagList.Web.Tests.Testing;
using Xunit;

public class SnagListTests : TestContext
{
    [Fact]
    public void LoadMore_appends_the_next_page_and_hides_once_NextCursor_is_null()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            var page = callCount == 1
                ? new PagedResponse<SnagSummaryResponse>(
                    [new SnagSummaryResponse { Id = Guid.NewGuid(), LocationId = Guid.NewGuid(), SubLocation = "A", Category = default, Severity = default, Status = default, ReportedByName = "Jane", ReportedAt = DateTimeOffset.UtcNow, Version = 1, Links = new Dictionary<string, ApiLink>() }],
                    "cursor-1")
                : new PagedResponse<SnagSummaryResponse>(
                    [new SnagSummaryResponse { Id = Guid.NewGuid(), LocationId = Guid.NewGuid(), SubLocation = "B", Category = default, Severity = default, Status = default, ReportedByName = "Bob", ReportedAt = DateTimeOffset.UtcNow, Version = 1, Links = new Dictionary<string, ApiLink>() }],
                    null);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(page) };
        });
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));

        var component = RenderComponent<global::SnagList.Web.Pages.SnagList>();
        Assert.Single(component.FindAll("tbody tr"));
        Assert.NotNull(component.Find("button#load-more"));

        component.Find("button#load-more").Click();

        Assert.Equal(2, component.FindAll("tbody tr").Count);
        Assert.Empty(component.FindAll("button#load-more"));
    }
}
