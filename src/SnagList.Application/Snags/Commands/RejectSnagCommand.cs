namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record RejectSnagCommand(
    Guid SnagId, string ActingStaffId, string ActingStaffName, string Reason, int ExpectedVersion);

public sealed class RejectSnagCommandHandler(ISnagRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(RejectSnagCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);
        if (snag.Version != command.ExpectedVersion)
        {
            throw new SnagVersionConflictException(snag.Id, command.ExpectedVersion, snag.Version);
        }

        snag.AddComment(command.ActingStaffId, command.ActingStaffName, command.Reason, clock.UtcNow);
        snag.TransitionTo(SnagStatus.Rejected, command.ActingStaffId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
