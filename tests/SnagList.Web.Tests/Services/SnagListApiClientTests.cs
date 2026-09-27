namespace SnagList.Web.Tests.Services;

using System.Net;
using System.Net.Http.Json;
using SnagList.Contracts;
using SnagList.Contracts.Snags;
using SnagList.Domain.Snags;
using SnagList.Web.Services;
using SnagList.Web.Tests.Testing;
using Xunit;

public class SnagListApiClientTests
{
    private static SnagListApiClient BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(respond);
        return new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
    }

    [Fact]
    public async Task ListSnagsAsync_builds_the_expected_query_string()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PagedResponse<SnagSummaryResponse>([], null)),
        }, out var handler);

        await client.ListSnagsAsync(new SnagListFilter(Status: SnagStatus.Reported, Limit: 10), default);

        Assert.Contains("status=Reported", handler.LastRequest!.RequestUri!.Query);
        Assert.Contains("limit=10", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task ReportSnagAsync_posts_and_returns_the_new_id()
    {
        var newId = Guid.NewGuid();
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new { id = newId }),
        }, out var handler);

        var id = await client.ReportSnagAsync(
            new ReportSnagRequest(Guid.NewGuid(), "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium, "desc"), default);

        Assert.Equal(newId, id);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task EditSnagAsync_sends_a_PATCH_request()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NoContent), out var handler);

        await client.EditSnagAsync(Guid.NewGuid(),
            new EditSnagRequest("4th floor", SnagCategory.Other, SnagSeverity.Low, "desc", 1), default);

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task GetLocationAsync_returns_null_on_404_rather_than_throwing()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound), out _);

        var result = await client.GetLocationAsync(Guid.NewGuid(), default);

        Assert.Null(result);
    }

    [Fact]
    public async Task UploadSnagPhotoAsync_sends_multipart_form_data()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NoContent), out var handler);
        using var content = new MemoryStream([1, 2, 3]);

        await client.UploadSnagPhotoAsync(Guid.NewGuid(), "light.jpg", "image/jpeg", content, default);

        Assert.IsType<MultipartFormDataContent>(handler.LastRequest!.Content);
    }
}
