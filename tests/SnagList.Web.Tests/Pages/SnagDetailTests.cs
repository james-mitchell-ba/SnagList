namespace SnagList.Web.Tests.Pages;

using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
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
    public void Photos_offer_a_file_picker_and_a_rear_camera_option()
    {
        var snagId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Detail(snagId, new Dictionary<string, ApiLink>())) });
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));

        var component = RenderComponent<global::SnagList.Web.Pages.SnagDetail>(p => p.Add(x => x.SnagId, snagId));

        var filePicker = component.Find("#photo-upload");
        Assert.Null(filePicker.GetAttribute("accept"));
        Assert.Null(filePicker.GetAttribute("capture"));

        var camera = component.Find("#photo-camera");
        Assert.Equal("image/*", camera.GetAttribute("accept"));
        Assert.Equal("environment", camera.GetAttribute("capture"));
        Assert.Equal(2, component.FindAll("input[type=file]").Count);
    }

    [Fact]
    public void Photo_actions_show_distinct_button_text_instead_of_native_file_picker_text()
    {
        var snagId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Detail(snagId, new Dictionary<string, ApiLink>())) });
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));

        var component = RenderComponent<global::SnagList.Web.Pages.SnagDetail>(p => p.Add(x => x.SnagId, snagId));

        var uploadLabel = component.Find("label[for='photo-upload']");
        var cameraLabel = component.Find("label[for='photo-camera']");
        Assert.Equal("Upload a photo", uploadLabel.TextContent.Trim());
        Assert.Equal("Take a photo", cameraLabel.TextContent.Trim());
        Assert.Contains("btn", uploadLabel.ClassList);
        Assert.Contains("btn", cameraLabel.ClassList);
        Assert.Contains("visually-hidden", component.Find("#photo-upload").ClassList);
        Assert.Contains("visually-hidden", component.Find("#photo-camera").ClassList);
    }

    [Fact]
    public void Camera_photo_uses_the_existing_upload_and_reloads_the_Snag()
    {
        var snagId = Guid.NewGuid();
        var getCalls = 0;
        var uploadCalls = 0;
        HttpMethod? uploadedMethod = null;
        string? uploadedPath = null;
        string? uploadedBody = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                getCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Detail(snagId, new Dictionary<string, ApiLink>())),
                };
            }

            uploadCalls++;
            uploadedMethod = request.Method;
            uploadedPath = request.RequestUri?.AbsolutePath;
            uploadedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));

        var component = RenderComponent<global::SnagList.Web.Pages.SnagDetail>(p => p.Add(x => x.SnagId, snagId));
        var camera = component.FindComponents<InputFile>().Single(input => input.Find("input").Id == "photo-camera");

        camera.UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "snapshot.jpg", contentType: "image/jpeg"));

        Assert.Equal(1, uploadCalls);
        Assert.Equal(HttpMethod.Post, uploadedMethod);
        Assert.Equal($"/api/v1/snags/{snagId}/photos", uploadedPath);
        Assert.Contains("snapshot.jpg", uploadedBody);
        Assert.Equal(2, getCalls);
    }

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
