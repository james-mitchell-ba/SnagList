namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record WithdrawSnagCommand(Guid SnagId, string ActingStaffId, int ExpectedVersion);

public sealed class WithdrawSnagCommandHandler(ISnagRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(WithdrawSnagCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);

        if (snag.ReportedByStaffId != command.ActingStaffId)
        {
            throw new UnauthorizedSnagActionException("withdraw");
        }
        if (snag.Version != command.ExpectedVersion)
        {
            throw new SnagVersionConflictException(snag.Id, command.ExpectedVersion, snag.Version);
        }

        snag.TransitionTo(SnagStatus.Withdrawn, command.ActingStaffId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
