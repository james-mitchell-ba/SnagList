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

public class LocationsTests : TestContext
{
    [Fact]
    public void Retire_button_only_appears_for_a_Location_whose_Links_carry_it()
    {
        var withRetire = new LocationResponse
        {
            Id = Guid.NewGuid(), Name = "Head Office", Address = "1 Main St", IsActive = true,
            Links = new Dictionary<string, ApiLink> { ["retire"] = new("/x", "POST", "RetireLocation") },
        };
        var withoutRetire = new LocationResponse
        {
            Id = Guid.NewGuid(), Name = "Old Depot", Address = "9 Yard Ln", IsActive = false,
            Links = new Dictionary<string, ApiLink>(),
        };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PagedResponse<LocationResponse>([withRetire, withoutRetire], null)),
        });
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));

        var component = RenderComponent<global::SnagList.Web.Pages.Locations>();

        Assert.Single(component.FindAll("button.retire"));
    }

    [Fact]
    public void Creating_a_Location_shows_a_message_on_403_rather_than_throwing()
    {
        var handler = new FakeHttpMessageHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PagedResponse<LocationResponse>([], null)) }
            : new HttpResponseMessage(HttpStatusCode.Forbidden));
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));

        var component = RenderComponent<global::SnagList.Web.Pages.Locations>();
        component.Find("button#new-location").Click();
        component.Find("input#name").Change("Test Site");
        component.Find("input#address").Change("1 Test St");
        component.Find("form").Submit();

        Assert.Contains("Maintenance role", component.Markup);
    }
}
