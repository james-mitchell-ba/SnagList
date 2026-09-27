namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record EditSnagCommand(
    Guid SnagId, string ActingStaffId, string SubLocation, SnagCategory Category,
    SnagSeverity Severity, string Description, int ExpectedVersion);

public sealed class EditSnagCommandHandler(ISnagRepository repository, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(EditSnagCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);

        if (snag.ReportedByStaffId != command.ActingStaffId)
        {
            throw new UnauthorizedSnagActionException("edit");
        }
        if (snag.Version != command.ExpectedVersion)
        {
            throw new SnagVersionConflictException(snag.Id, command.ExpectedVersion, snag.Version);
        }

        snag.Edit(command.SubLocation, command.Category, command.Severity, command.Description);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
