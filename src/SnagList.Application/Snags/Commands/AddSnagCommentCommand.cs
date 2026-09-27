namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;

public sealed record AddSnagCommentCommand(Guid SnagId, string AuthorStaffId, string AuthorName, string Body);

public sealed class AddSnagCommentCommandHandler(ISnagRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(AddSnagCommentCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);
        snag.AddComment(command.AuthorStaffId, command.AuthorName, command.Body, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
