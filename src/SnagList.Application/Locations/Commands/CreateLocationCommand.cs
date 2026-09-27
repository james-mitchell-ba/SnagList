namespace SnagList.Application.Locations.Commands;

using SnagList.Application.Abstractions;
using SnagList.Domain.Locations;

public sealed record CreateLocationCommand(string Name, string Address);

public sealed class CreateLocationCommandHandler(ILocationRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<Guid> HandleAsync(CreateLocationCommand command, CancellationToken ct)
    {
        var location = Location.Create(command.Name, command.Address);
        repository.Add(location);
        await unitOfWork.SaveChangesAsync(ct);
        return location.Id;
    }
}
