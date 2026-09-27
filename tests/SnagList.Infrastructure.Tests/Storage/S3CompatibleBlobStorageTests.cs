namespace SnagList.Infrastructure.Tests.Storage;

using SnagList.Infrastructure.Storage;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("S3Mock")]
public class S3CompatibleBlobStorageTests(S3MockFixture fixture)
{
    [Fact]
    public async Task PutAsync_then_GetAsync_round_trips_bytes()
    {
        var storage = new S3CompatibleBlobStorage(fixture.CreateClient(), S3MockFixture.BucketName);
        var original = new byte[] { 1, 2, 3, 4 };
        using var content = new MemoryStream(original);

        await storage.PutAsync("snags/test/photo.jpg", content, "image/jpeg", default);

        await using var readBack = await storage.GetAsync("snags/test/photo.jpg", default);
        using var buffer = new MemoryStream();
        await readBack.CopyToAsync(buffer);
        Assert.Equal(original, buffer.ToArray());
    }

    [Fact]
    public async Task GetPresignedGetUrlAsync_returns_a_url_pointing_at_the_key()
    {
        var storage = new S3CompatibleBlobStorage(fixture.CreateClient(), S3MockFixture.BucketName);
        using var content = new MemoryStream([1]);
        await storage.PutAsync("snags/test/photo2.jpg", content, "image/jpeg", default);

        var url = await storage.GetPresignedGetUrlAsync("snags/test/photo2.jpg", TimeSpan.FromMinutes(5), default);

        Assert.Contains("photo2.jpg", url.ToString());
    }

    [Fact]
    public async Task DeleteAsync_removes_the_object()
    {
        var storage = new S3CompatibleBlobStorage(fixture.CreateClient(), S3MockFixture.BucketName);
        using var content = new MemoryStream([1]);
        await storage.PutAsync("snags/test/photo3.jpg", content, "image/jpeg", default);

        await storage.DeleteAsync("snags/test/photo3.jpg", default);

        await Assert.ThrowsAnyAsync<Exception>(() => storage.GetAsync("snags/test/photo3.jpg", default));
    }
}
