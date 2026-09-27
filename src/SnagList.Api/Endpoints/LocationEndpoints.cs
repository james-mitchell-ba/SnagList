namespace SnagList.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using SnagList.Authorization;
using SnagList.Contracts;
using SnagList.Contracts.Locations;
using SnagList.Api.Hypermedia;
using SnagList.Application.Abstractions;
using SnagList.Application.Locations.Commands;
using SnagList.Application.Locations.Queries;

public static class LocationEndpoints
{
    public static IEndpointRouteBuilder MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/locations").RequireAuthorization(PolicyNames.Staff);

        group.MapGet("", async (
            ListLocationsQueryHandler handler, HttpContext http, CancellationToken ct,
            [FromQuery] bool includeRetired = false, [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            var page = await handler.HandleAsync(
                new ListLocationsQuery(includeRetired, cursor, limit is > 0 and <= 100 ? limit : 20), ct);
            var roles = http.User.GetStaffRoles();
            var items = page.Items.Select(l => new LocationResponse
            {
                Id = l.Id,
                Name = l.Name,
                Address = l.Address,
                IsActive = l.IsActive,
                Links = LocationLinksBuilder.Build(l.Id, l.IsActive, roles),
            }).ToList();
            return Results.Ok(new PagedResponse<LocationResponse>(items, page.NextCursor));
        }).WithName("ListLocations");

        group.MapGet("/{id:guid}", async (Guid id, ILocationRepository repository, HttpContext http, CancellationToken ct) =>
        {
            var location = await repository.GetAsync(id, ct);
            if (location is null) return Results.NotFound();

            var roles = http.User.GetStaffRoles();
            return Results.Ok(new LocationResponse
            {
                Id = location.Id,
                Name = location.Name,
                Address = location.Address,
                IsActive = location.IsActive,
                Links = LocationLinksBuilder.Build(location.Id, location.IsActive, roles),
            });
        }).WithName("GetLocation");

        group.MapPost("", async (CreateLocationRequest request, CreateLocationCommandHandler handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateLocationCommand(request.Name, request.Address), ct);
            return Results.Created($"/api/v1/locations/{id}", new { id });
        }).RequireAuthorization(PolicyNames.Maintenance).WithName("CreateLocation");

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateLocationRequest request, UpdateLocationCommandHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UpdateLocationCommand(id, request.Name, request.Address), ct);
            return Results.NoContent();
        }).RequireAuthorization(PolicyNames.Maintenance).WithName("UpdateLocation");

        group.MapPost("/{id:guid}/retire", async (Guid id, RetireLocationCommandHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RetireLocationCommand(id), ct);
            return Results.NoContent();
        }).RequireAuthorization(PolicyNames.Maintenance).WithName("RetireLocation");

        return app;
    }
}
