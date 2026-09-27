namespace SnagList.Web.Services;

using System.Net;
using System.Net.Http.Json;
using SnagList.Contracts;
using SnagList.Contracts.Locations;
using SnagList.Contracts.Snags;

public sealed class SnagListApiClient(HttpClient httpClient)
{
    private sealed record CreatedIdResponse(Guid Id);

    public async Task<PagedResponse<LocationResponse>> ListLocationsAsync(bool includeRetired, string? cursor, int limit, CancellationToken ct)
    {
        var query = $"?includeRetired={includeRetired}&limit={limit}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
        return await httpClient.GetFromJsonAsync<PagedResponse<LocationResponse>>($"api/v1/locations{query}", ct) ?? new([], null);
    }

    public async Task<LocationResponse?> GetLocationAsync(Guid id, CancellationToken ct)
    {
        var response = await httpClient.GetAsync($"api/v1/locations/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LocationResponse>(cancellationToken: ct);
    }

    public async Task<Guid> CreateLocationAsync(CreateLocationRequest request, CancellationToken ct)
    {
        var response = await httpClient.PostAsJsonAsync("api/v1/locations", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedIdResponse>(cancellationToken: ct))!.Id;
    }

    public async Task UpdateLocationAsync(Guid id, UpdateLocationRequest request, CancellationToken ct) =>
        (await httpClient.PutAsJsonAsync($"api/v1/locations/{id}", request, ct)).EnsureSuccessStatusCode();

    public async Task RetireLocationAsync(Guid id, CancellationToken ct) =>
        (await httpClient.PostAsync($"api/v1/locations/{id}/retire", null, ct)).EnsureSuccessStatusCode();

    public async Task<PagedResponse<SnagSummaryResponse>> ListSnagsAsync(SnagListFilter filter, CancellationToken ct) =>
        await httpClient.GetFromJsonAsync<PagedResponse<SnagSummaryResponse>>($"api/v1/snags{filter.ToQueryString()}", ct)
            ?? new([], null);

    public async Task<SnagDetailResponse?> GetSnagAsync(Guid id, CancellationToken ct)
    {
        var response = await httpClient.GetAsync($"api/v1/snags/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SnagDetailResponse>(cancellationToken: ct);
    }

    public async Task<Guid> ReportSnagAsync(ReportSnagRequest request, CancellationToken ct)
    {
        var response = await httpClient.PostAsJsonAsync("api/v1/snags", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedIdResponse>(cancellationToken: ct))!.Id;
    }

    public async Task EditSnagAsync(Guid id, EditSnagRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Patch, $"api/v1/snags/{id}") { Content = JsonContent.Create(request) };
        (await httpClient.SendAsync(message, ct)).EnsureSuccessStatusCode();
    }

    public Task WithdrawSnagAsync(Guid id, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/withdraw", new WithdrawSnagRequest(expectedVersion), ct);

    public Task AcknowledgeSnagAsync(Guid id, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/acknowledge", new ChangeSnagStatusRequest(expectedVersion), ct);

    public Task StartSnagWorkAsync(Guid id, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/start", new ChangeSnagStatusRequest(expectedVersion), ct);

    public Task ResolveSnagAsync(Guid id, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/resolve", new ChangeSnagStatusRequest(expectedVersion), ct);

    public Task CloseSnagAsync(Guid id, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/close", new ChangeSnagStatusRequest(expectedVersion), ct);

    public Task RejectSnagAsync(Guid id, string reason, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/reject", new RejectSnagRequest(reason, expectedVersion), ct);

    public Task AddSnagCommentAsync(Guid id, string body, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/comments", new AddSnagCommentRequest(body), ct);

    public async Task UploadSnagPhotoAsync(Guid id, string fileName, string contentType, Stream content, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "file", fileName);
        (await httpClient.PostAsync($"api/v1/snags/{id}/photos", form, ct)).EnsureSuccessStatusCode();
    }

    private async Task PostAsync<TRequest>(string url, TRequest request, CancellationToken ct) =>
        (await httpClient.PostAsJsonAsync(url, request, ct)).EnsureSuccessStatusCode();
}
