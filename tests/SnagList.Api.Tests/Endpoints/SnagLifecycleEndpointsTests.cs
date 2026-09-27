namespace SnagList.Api.Tests.Endpoints;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SnagList.Api.Contracts.Locations;
using SnagList.Api.Contracts.Snags;
using SnagList.Api.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class SnagLifecycleEndpointsTests(SnagListApiFactory factory) : IClassFixture<SnagListApiFactory>
{
    private sealed record CreatedResource(Guid Id);

    private async Task<Guid> CreateLocationAsync(string name)
    {
        var client = factory.CreateAuthenticatedClient("U900010", "Maintenance");
        var response = await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest(name, "1 Main St"));
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private async Task<Guid> ReportSnagAsync(Guid locationId, string reporterStaffId)
    {
        var reporter = factory.CreateAuthenticatedClient(reporterStaffId, "Staff");
        var response = await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium, "desc"));
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    [Fact]
    public async Task Maintenance_can_drive_a_Snag_through_its_full_lifecycle()
    {
        var locationId = await CreateLocationAsync("Head Office F");
        var snagId = await ReportSnagAsync(locationId, "U100020");
        var maintenance = factory.CreateAuthenticatedClient("U900011", "Maintenance");

        Assert.Equal(HttpStatusCode.NoContent,
            (await maintenance.PostAsJsonAsync($"/api/v1/snags/{snagId}/acknowledge", new ChangeSnagStatusRequest(1))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await maintenance.PostAsJsonAsync($"/api/v1/snags/{snagId}/start", new ChangeSnagStatusRequest(2))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await maintenance.PostAsJsonAsync($"/api/v1/snags/{snagId}/resolve", new ChangeSnagStatusRequest(3))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await maintenance.PostAsJsonAsync($"/api/v1/snags/{snagId}/close", new ChangeSnagStatusRequest(4))).StatusCode);

        var detail = await maintenance.GetFromJsonAsync<SnagDetailResponse>($"/api/v1/snags/{snagId}");
        Assert.Equal(SnagStatus.Closed, detail!.Status);
    }

    [Fact]
    public async Task Staff_cannot_acknowledge_a_Snag()
    {
        var locationId = await CreateLocationAsync("Head Office G");
        var snagId = await ReportSnagAsync(locationId, "U100021");
        var staff = factory.CreateAuthenticatedClient("U100021", "Staff");

        var response = await staff.PostAsJsonAsync($"/api/v1/snags/{snagId}/acknowledge", new ChangeSnagStatusRequest(1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Maintenance_can_reject_with_a_reason_recorded_as_a_comment()
    {
        var locationId = await CreateLocationAsync("Head Office H");
        var snagId = await ReportSnagAsync(locationId, "U100022");
        var maintenance = factory.CreateAuthenticatedClient("U900012", "Maintenance");

        var response = await maintenance.PostAsJsonAsync($"/api/v1/snags/{snagId}/reject", new RejectSnagRequest("Duplicate report", 1));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await maintenance.GetFromJsonAsync<SnagDetailResponse>($"/api/v1/snags/{snagId}");
        Assert.Equal(SnagStatus.Rejected, detail!.Status);
        Assert.Contains(detail.Comments, c => c.Body == "Duplicate report");
    }

    [Fact]
    public async Task Either_role_can_add_a_comment()
    {
        var locationId = await CreateLocationAsync("Head Office I");
        var snagId = await ReportSnagAsync(locationId, "U100023");
        var reporter = factory.CreateAuthenticatedClient("U100023", "Staff");

        var response = await reporter.PostAsJsonAsync($"/api/v1/snags/{snagId}/comments", new AddSnagCommentRequest("Any update?"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_a_photo_then_downloading_it_redirects_to_a_presigned_url()
    {
        var locationId = await CreateLocationAsync("Head Office J");
        var snagId = await ReportSnagAsync(locationId, "U100024");
        var reporter = factory.CreateAuthenticatedClient("U100024", "Staff");

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3, 4]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "file", "light.jpg");
        var uploadResponse = await reporter.PostAsync($"/api/v1/snags/{snagId}/photos", form);
        Assert.Equal(HttpStatusCode.NoContent, uploadResponse.StatusCode);

        var detail = await reporter.GetFromJsonAsync<SnagDetailResponse>($"/api/v1/snags/{snagId}");
        var photo = Assert.Single(detail!.Photos);

        var noRedirectClient = factory.CreateAuthenticatedClientNoRedirect("U100024", "Staff");
        var downloadResponse = await noRedirectClient.GetAsync(photo.Href.Href);

        Assert.Equal(HttpStatusCode.Redirect, downloadResponse.StatusCode);
        Assert.Contains(photo.PhotoKey, downloadResponse.Headers.Location!.ToString());
    }
}
