namespace SnagList.Mcp.Tools;

using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using SnagList.Application.Abstractions;
using SnagList.Application.Locations.Commands;
using SnagList.Application.Locations.Queries;
using SnagList.Authorization;

public sealed record CreateLocationResult(Guid Id);
public sealed record LocationResult(Guid Id, string Name, string Address, bool IsActive);
public sealed record ListLocationsResult(IReadOnlyList<LocationSummary> Items, string? NextCursor);

[McpServerToolType]
public sealed class LocationTools(
    IHttpContextAccessor httpContextAccessor,
    ILocationRepository locationRepository,
    CreateLocationCommandHandler createHandler,
    UpdateLocationCommandHandler updateHandler,
    RetireLocationCommandHandler retireHandler,
    ListLocationsQueryHandler listHandler)
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext!.User;

    private void RequireMaintenance()
    {
        if (!AuthorizationPolicies.IsMaintenance(User.GetStaffRoles()))
        {
            throw new UnauthorizedAccessException("This operation requires the Maintenance role.");
        }
    }

    [McpServerTool(Name = "list_locations")]
    [Description("Lists corporate Locations Snags can be reported against, cursor-paginated.")]
    public async Task<ListLocationsResult> ListLocations(
        [Description("Include retired Locations. Defaults to false.")] bool includeRetired,
        [Description("Opaque cursor from a previous call's nextCursor, or omit for the first page.")] string? cursor,
        [Description("Max items to return, 1-100. Defaults to 20.")] int limit,
        CancellationToken ct)
    {
        var page = await listHandler.HandleAsync(
            new ListLocationsQuery(includeRetired, cursor, limit is > 0 and <= 100 ? limit : 20), ct);
        return new ListLocationsResult(page.Items, page.NextCursor);
    }

    [McpServerTool(Name = "get_location")]
    [Description("Gets a single Location by id.")]
    public async Task<LocationResult> GetLocation(Guid locationId, CancellationToken ct)
    {
        var location = await locationRepository.GetAsync(locationId, ct)
            ?? throw new InvalidOperationException($"Location {locationId} was not found.");
        return new LocationResult(location.Id, location.Name, location.Address, location.IsActive);
    }

    [McpServerTool(Name = "create_location")]
    [Description("Adds a new corporate site that Snags can be reported against. Requires the Maintenance role.")]
    public async Task<CreateLocationResult> CreateLocation(string name, string address, CancellationToken ct)
    {
        RequireMaintenance();
        var id = await createHandler.HandleAsync(new CreateLocationCommand(name, address), ct);
        return new CreateLocationResult(id);
    }

    [McpServerTool(Name = "update_location")]
    [Description("Edits an existing Location's name or address. Requires the Maintenance role.")]
    public Task UpdateLocation(Guid locationId, string name, string address, CancellationToken ct)
    {
        RequireMaintenance();
        return updateHandler.HandleAsync(new UpdateLocationCommand(locationId, name, address), ct);
    }

    [McpServerTool(Name = "retire_location")]
    [Description("Retires a Location, hiding it from new Snag reports without deleting history. Requires the Maintenance role.")]
    public Task RetireLocation(Guid locationId, CancellationToken ct)
    {
        RequireMaintenance();
        return retireHandler.HandleAsync(new RetireLocationCommand(locationId), ct);
    }
}
