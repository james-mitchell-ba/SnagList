namespace SnagList.Application.Locations.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Locations;

public sealed record RetireLocationCommand(Guid Id);

public sealed class RetireLocationCommandHandler(ILocationRepository repository, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(RetireLocationCommand command, CancellationToken ct)
    {
        var location = await repository.GetAsync(command.Id, ct)
            ?? throw new LocationNotFoundException(command.Id);
        location.Retire();
        await unitOfWork.SaveChangesAsync(ct);
    }
}
