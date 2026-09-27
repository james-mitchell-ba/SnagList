namespace SnagList.Api.Endpoints;

using SnagList.Authorization;
using SnagList.Domain.Staff;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me", (HttpContext http) => Results.Ok(new MeResponse(
                http.User.GetStaffId(), http.User.GetStaffName(), http.User.GetStaffEmail(), http.User.GetStaffRoles())))
            .RequireAuthorization(PolicyNames.Staff)
            .WithName("GetMe");
        return app;
    }
}

public sealed record MeResponse(string StaffId, string Name, string Email, IReadOnlyList<StaffRole> Roles);
