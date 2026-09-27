namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class UploadSnagPhotoCommandTests
{
    [Fact]
    public async Task Uploads_bytes_to_blob_storage_and_attaches_the_photo()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
            "Flickering light", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var blobStorage = new FakeBlobStorage();
        var handler = new UploadSnagPhotoCommandHandler(repo, blobStorage, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));
        using var content = new MemoryStream([1, 2, 3]);

        await handler.HandleAsync(
            new UploadSnagPhotoCommand(snag.Id, "light.jpg", "image/jpeg", content, 3), default);

        var photo = Assert.Single(repo.Store[snag.Id].Photos);
        Assert.Equal("light.jpg", photo.FileName);
        Assert.StartsWith($"snags/{snag.Id}/", photo.BlobKey);
        Assert.Single(blobStorage.PutCalls);
        Assert.Equal("image/jpeg", blobStorage.PutCalls[0].ContentType);
    }

    [Fact]
    public async Task Throws_SnagPhotoLimitExceededException_on_the_sixth_photo()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
            "Flickering light", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new UploadSnagPhotoCommandHandler(repo, new FakeBlobStorage(), new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));
        for (var i = 0; i < 5; i++)
        {
            using var c = new MemoryStream([1]);
            await handler.HandleAsync(new UploadSnagPhotoCommand(snag.Id, $"p{i}.jpg", "image/jpeg", c, 1), default);
        }

        using var sixth = new MemoryStream([1]);
        await Assert.ThrowsAsync<SnagPhotoLimitExceededException>(
            () => handler.HandleAsync(new UploadSnagPhotoCommand(snag.Id, "p6.jpg", "image/jpeg", sixth, 1), default));
    }
}
