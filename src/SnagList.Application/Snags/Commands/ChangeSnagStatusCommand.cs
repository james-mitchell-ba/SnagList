namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record ChangeSnagStatusCommand(Guid SnagId, SnagStatus TargetStatus, string ActingStaffId, int ExpectedVersion);

public sealed class ChangeSnagStatusCommandHandler(ISnagRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(ChangeSnagStatusCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);
        if (snag.Version != command.ExpectedVersion)
        {
            throw new SnagVersionConflictException(snag.Id, command.ExpectedVersion, snag.Version);
        }

        snag.TransitionTo(command.TargetStatus, command.ActingStaffId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
