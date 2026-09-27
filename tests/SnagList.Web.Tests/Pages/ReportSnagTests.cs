namespace SnagList.Web.Tests.Pages;

using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SnagList.Contracts;
using SnagList.Contracts.Locations;
using SnagList.Web.Services;
using SnagList.Web.Tests.Testing;
using Xunit;

public class ReportSnagTests : TestContext
{
    [Fact]
    public void Submitting_the_form_posts_the_report_and_navigates_to_the_new_Snag()
    {
        var locationId = Guid.NewGuid();
        var newSnagId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PagedResponse<LocationResponse>(
                        [new LocationResponse { Id = locationId, Name = "Head Office", Address = "1 Main St", IsActive = true, Links = new Dictionary<string, ApiLink>() }],
                        null)),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new { id = newSnagId }) };
        });
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));
        var navigationManager = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

        var component = RenderComponent<global::SnagList.Web.Pages.ReportSnag>();
        component.Find("textarea#description").Change("Flickering light");
        component.Find("form").Submit();

        Assert.EndsWith($"/snags/{newSnagId}", navigationManager.Uri);
    }
}
