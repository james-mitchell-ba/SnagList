namespace SnagList.Web.Tests.Pages;

using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SnagList.Contracts;
using SnagList.Contracts.Snags;
using SnagList.Domain.Snags;
using SnagList.Web.Services;
using SnagList.Web.Tests.Testing;
using Xunit;

public class SnagDetailTests : TestContext
{
    private static SnagDetailResponse Detail(
        Guid id, IReadOnlyDictionary<string, ApiLink> links, SnagStatus status = SnagStatus.Reported) => new()
    {
        Id = id, LocationId = Guid.NewGuid(), SubLocation = "3rd floor", Category = SnagCategory.Electrical,
        Severity = SnagSeverity.Medium, Description = "Flickering light", Status = status,
        ReportedByStaffId = "U1", ReportedByName = "Jane", ReportedAt = DateTimeOffset.UtcNow, Version = 1,
        Comments = [], Photos = [], Links = links,
    };

    [Fact]
    public void Clicking_Acknowledge_calls_the_API_and_reloads_the_updated_Snag()
    {
        var snagId = Guid.NewGuid();
        var reportedLinks = new Dictionary<string, ApiLink> { ["acknowledge"] = new($"/api/v1/snags/{snagId}/acknowledge", "POST", "AcknowledgeSnag") };
        var acknowledgedLinks = new Dictionary<string, ApiLink> { ["resolve"] = new($"/api/v1/snags/{snagId}/resolve", "POST", "ResolveSnag") };
        var getCallCount = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                getCallCount++;
                var detail = getCallCount == 1
                    ? Detail(snagId, reportedLinks)
                    : Detail(snagId, acknowledgedLinks, SnagStatus.Acknowledged);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(detail) };
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));

        var component = RenderComponent<global::SnagList.Web.Pages.SnagDetail>(p => p.Add(x => x.SnagId, snagId));
        Assert.NotEmpty(component.FindAll("#action-acknowledge"));
        Assert.Empty(component.FindAll("#action-resolve"));

        component.Find("#action-acknowledge").Click();

        Assert.NotEmpty(component.FindAll("#action-resolve"));
        Assert.Empty(component.FindAll("#action-acknowledge"));
    }
}
