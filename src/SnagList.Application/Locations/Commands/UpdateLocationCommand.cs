namespace SnagList.Application.Locations.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Locations;

public sealed record UpdateLocationCommand(Guid Id, string Name, string Address);

public sealed class UpdateLocationCommandHandler(ILocationRepository repository, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(UpdateLocationCommand command, CancellationToken ct)
    {
        var location = await repository.GetAsync(command.Id, ct)
            ?? throw new LocationNotFoundException(command.Id);
        location.Update(command.Name, command.Address);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
