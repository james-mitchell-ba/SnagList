namespace SnagList.Application.Staff.Commands;

using SnagList.Application.Abstractions;
using SnagList.Domain.Staff;

public sealed record SyncStaffIdentityCommand(string StaffId, string Name, string Email, IReadOnlyList<StaffRole> Roles);

public sealed class SyncStaffIdentityCommandHandler(IStaffIdentityRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(SyncStaffIdentityCommand command, CancellationToken ct)
    {
        var existing = await repository.GetAsync(command.StaffId, ct);
        if (existing is null)
        {
            repository.Add(StaffIdentity.FirstSeen(command.StaffId, command.Name, command.Email, command.Roles, clock.UtcNow));
        }
        else
        {
            existing.Sync(command.Name, command.Email, command.Roles, clock.UtcNow);
        }
        await unitOfWork.SaveChangesAsync(ct);
    }
}
