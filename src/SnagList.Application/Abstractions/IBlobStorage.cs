namespace SnagList.Application.Abstractions;

public interface IBlobStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream> GetAsync(string key, CancellationToken ct);
    Task<Uri> GetPresignedGetUrlAsync(string key, TimeSpan expiry, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
