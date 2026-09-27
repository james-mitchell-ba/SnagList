namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Locations;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record ReportSnagCommand(
    Guid LocationId, string SubLocation, SnagCategory Category, SnagSeverity Severity,
    string Description, string ReportedByStaffId, string ReportedByName);

public sealed class ReportSnagCommandHandler(
    ILocationRepository locationRepository, ISnagRepository snagRepository, IUnitOfWork unitOfWork)
{
    public async Task<Guid> HandleAsync(ReportSnagCommand command, CancellationToken ct)
    {
        var location = await locationRepository.GetAsync(command.LocationId, ct)
            ?? throw new LocationNotFoundException(command.LocationId);
        if (!location.IsActive) throw new LocationNotActiveException(command.LocationId);

        var snag = Snag.Report(
            command.LocationId, command.SubLocation, command.Category, command.Severity,
            command.Description, command.ReportedByStaffId, command.ReportedByName, DateTimeOffset.UtcNow);

        snagRepository.Add(snag);
        await unitOfWork.SaveChangesAsync(ct);
        return snag.Id;
    }
}
