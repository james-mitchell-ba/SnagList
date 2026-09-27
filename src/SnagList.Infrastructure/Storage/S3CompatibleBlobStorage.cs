namespace SnagList.Infrastructure.Storage;

using Amazon.S3;
using Amazon.S3.Model;
using SnagList.Application.Abstractions;

public sealed class S3CompatibleBlobStorage(IAmazonS3 s3Client, string bucketName) : IBlobStorage
{
    public Task PutAsync(string key, Stream content, string contentType, CancellationToken ct) =>
        s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
        }, ct);

    public async Task<Stream> GetAsync(string key, CancellationToken ct)
    {
        var response = await s3Client.GetObjectAsync(bucketName, key, ct);
        return response.ResponseStream;
    }

    public Task<Uri> GetPresignedGetUrlAsync(string key, TimeSpan expiry, CancellationToken ct) =>
        Task.FromResult(new Uri(s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry),
        })));

    public Task DeleteAsync(string key, CancellationToken ct) =>
        s3Client.DeleteObjectAsync(bucketName, key, ct);
}
