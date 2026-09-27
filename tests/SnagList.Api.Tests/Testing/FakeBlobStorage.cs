namespace SnagList.Api.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class FakeBlobStorage : IBlobStorage
{
    public Task PutAsync(string key, Stream content, string contentType, CancellationToken ct) => Task.CompletedTask;

    public Task<Stream> GetAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());

    public Task<Uri> GetPresignedGetUrlAsync(string key, TimeSpan expiry, CancellationToken ct) =>
        Task.FromResult(new Uri($"https://fake-storage.test/{key}"));

    public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
}
