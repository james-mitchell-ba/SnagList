namespace SnagList.Api.Tests.Endpoints;

using System.Net;
using System.Net.Http.Json;
using SnagList.Contracts;
using SnagList.Contracts.Locations;
using SnagList.Contracts.Snags;
using SnagList.Api.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class SnagEndpointsTests(SnagListApiFactory factory) : IClassFixture<SnagListApiFactory>
{
    private async Task<Guid> CreateLocationAsync(string name)
    {
        var client = factory.CreateAuthenticatedClient("U900000", "Maintenance");
        var response = await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest(name, "1 Main St"));
        var created = await response.Content.ReadFromJsonAsync<CreatedResource>();
        return created!.Id;
    }

    [Fact]
    public async Task Reporter_can_report_then_get_their_Snag_with_edit_and_withdraw_links()
    {
        var locationId = await CreateLocationAsync("Head Office A");
        var reporter = factory.CreateAuthenticatedClient("U100010", "Staff");

        var reportResponse = await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium, "Flickering light"));
        Assert.Equal(HttpStatusCode.Created, reportResponse.StatusCode);
        var created = await reportResponse.Content.ReadFromJsonAsync<CreatedResource>();

        var detail = await reporter.GetFromJsonAsync<SnagDetailResponse>($"/api/v1/snags/{created!.Id}");

        Assert.Equal(SnagStatus.Reported, detail!.Status);
        Assert.Contains("edit", detail.Links.Keys);
        Assert.Contains("withdraw", detail.Links.Keys);
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        var locationId = await CreateLocationAsync("Head Office B");
        var reporter = factory.CreateAuthenticatedClient("U100011", "Staff");
        await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "4th floor", SnagCategory.Plumbing, SnagSeverity.Low, "Dripping tap"));

        var page = await reporter.GetFromJsonAsync<PagedResponse<SnagSummaryResponse>>(
            $"/api/v1/snags?locationId={locationId}&status=Acknowledged&limit=50");

        Assert.Empty(page!.Items);
    }

    [Fact]
    public async Task Reporter_can_edit_while_Reported_but_a_different_staff_member_cannot()
    {
        var locationId = await CreateLocationAsync("Head Office C");
        var reporter = factory.CreateAuthenticatedClient("U100012", "Staff");
        var reportResponse = await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "5th floor", SnagCategory.Other, SnagSeverity.Low, "Squeaky door"));
        var created = await reportResponse.Content.ReadFromJsonAsync<CreatedResource>();

        var editResponse = await reporter.PatchAsJsonAsync($"/api/v1/snags/{created!.Id}",
            new EditSnagRequest("5th floor, room 5.02", SnagCategory.Other, SnagSeverity.Low, "Squeaky door, worse now", 1));
        Assert.Equal(HttpStatusCode.NoContent, editResponse.StatusCode);

        var otherStaff = factory.CreateAuthenticatedClient("U100099", "Staff");
        var forbidden = await otherStaff.PatchAsJsonAsync($"/api/v1/snags/{created.Id}",
            new EditSnagRequest("tampering", SnagCategory.Other, SnagSeverity.Low, "tampering", 2));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Editing_with_a_stale_expectedVersion_returns_409_with_the_conflict_type()
    {
        var locationId = await CreateLocationAsync("Head Office D");
        var reporter = factory.CreateAuthenticatedClient("U100013", "Staff");
        var reportResponse = await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "6th floor", SnagCategory.Other, SnagSeverity.Low, "desc"));
        var created = await reportResponse.Content.ReadFromJsonAsync<CreatedResource>();

        var response = await reporter.PatchAsJsonAsync($"/api/v1/snags/{created!.Id}",
            new EditSnagRequest("6th floor", SnagCategory.Other, SnagSeverity.Low, "desc", ExpectedVersion: 999));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>();
        Assert.Equal("https://snaglist.example/errors/version-conflict", problem!.Type);
    }

    [Fact]
    public async Task Reporter_can_withdraw_while_Reported()
    {
        var locationId = await CreateLocationAsync("Head Office E");
        var reporter = factory.CreateAuthenticatedClient("U100014", "Staff");
        var reportResponse = await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "7th floor", SnagCategory.Other, SnagSeverity.Low, "desc"));
        var created = await reportResponse.Content.ReadFromJsonAsync<CreatedResource>();

        var response = await reporter.PostAsJsonAsync($"/api/v1/snags/{created!.Id}/withdraw", new WithdrawSnagRequest(1));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await reporter.GetFromJsonAsync<SnagDetailResponse>($"/api/v1/snags/{created.Id}");
        Assert.Equal(SnagStatus.Withdrawn, detail!.Status);
    }

    private sealed record CreatedResource(Guid Id);
    private sealed record ProblemDetailsBody(string Type);
}
