namespace SnagList.Api.Hypermedia;

using SnagList.Authorization;
using SnagList.Contracts;
using SnagList.Domain.Staff;

public static class LocationLinksBuilder
{
    public static IReadOnlyDictionary<string, ApiLink> Build(Guid locationId, bool isActive, IReadOnlyList<StaffRole> callerRoles)
    {
        var links = new Dictionary<string, ApiLink>
        {
            ["self"] = new($"/api/v1/locations/{locationId}", "GET", "GetLocation"),
        };

        if (AuthorizationPolicies.IsMaintenance(callerRoles))
        {
            links["update"] = new($"/api/v1/locations/{locationId}", "PUT", "UpdateLocation");
            if (isActive)
            {
                links["retire"] = new($"/api/v1/locations/{locationId}/retire", "POST", "RetireLocation");
            }
        }

        return links;
    }
}
