namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record UploadSnagPhotoCommand(Guid SnagId, string FileName, string ContentType, Stream Content, long SizeBytes);

public sealed class UploadSnagPhotoCommandHandler(
    ISnagRepository repository, IBlobStorage blobStorage, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(UploadSnagPhotoCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);

        var blobKey = $"snags/{command.SnagId}/{Guid.NewGuid()}-{command.FileName}";
        await blobStorage.PutAsync(blobKey, command.Content, command.ContentType, ct);

        snag.AddPhoto(new SnagPhoto(blobKey, command.FileName, command.ContentType, command.SizeBytes, clock.UtcNow));
        await unitOfWork.SaveChangesAsync(ct);
    }
}
