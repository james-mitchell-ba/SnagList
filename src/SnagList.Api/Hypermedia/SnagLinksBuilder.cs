namespace SnagList.Api.Hypermedia;

using SnagList.Api.Authorization;
using SnagList.Api.Contracts;
using SnagList.Domain.Snags;
using SnagList.Domain.Staff;

public static class SnagLinksBuilder
{
    public static IReadOnlyDictionary<string, ApiLink> Build(
        Guid snagId, SnagStatus status, string reportedByStaffId,
        string callerStaffId, IReadOnlyList<StaffRole> callerRoles)
    {
        var links = new Dictionary<string, ApiLink>
        {
            ["self"] = new($"/api/v1/snags/{snagId}", "GET", "GetSnag"),
        };

        var isReporter = reportedByStaffId == callerStaffId;
        var isMaintenance = AuthorizationPolicies.IsMaintenance(callerRoles);

        if (status == SnagStatus.Reported && isReporter)
        {
            links["edit"] = new($"/api/v1/snags/{snagId}", "PATCH", "EditSnag");
            links["withdraw"] = new($"/api/v1/snags/{snagId}/withdraw", "POST", "WithdrawSnag");
        }

        if (isMaintenance)
        {
            switch (status)
            {
                case SnagStatus.Reported:
                    links["acknowledge"] = new($"/api/v1/snags/{snagId}/acknowledge", "POST", "AcknowledgeSnag");
                    links["reject"] = new($"/api/v1/snags/{snagId}/reject", "POST", "RejectSnag");
                    break;
                case SnagStatus.Acknowledged:
                    links["start"] = new($"/api/v1/snags/{snagId}/start", "POST", "StartSnagWork");
                    links["reject"] = new($"/api/v1/snags/{snagId}/reject", "POST", "RejectSnag");
                    break;
                case SnagStatus.InProgress:
                    links["resolve"] = new($"/api/v1/snags/{snagId}/resolve", "POST", "ResolveSnag");
                    break;
                case SnagStatus.Resolved:
                    links["close"] = new($"/api/v1/snags/{snagId}/close", "POST", "CloseSnag");
                    break;
            }
        }

        links["comments"] = new($"/api/v1/snags/{snagId}/comments", "POST", "AddSnagComment");
        links["photos"] = new($"/api/v1/snags/{snagId}/photos", "POST", "UploadSnagPhoto");

        return links;
    }

    public static IReadOnlyDictionary<string, ApiLink> BuildSummaryLinks(Guid snagId) => new Dictionary<string, ApiLink>
    {
        ["self"] = new($"/api/v1/snags/{snagId}", "GET", "GetSnag"),
        ["comments"] = new($"/api/v1/snags/{snagId}/comments", "POST", "AddSnagComment"),
        ["photos"] = new($"/api/v1/snags/{snagId}/photos", "POST", "UploadSnagPhoto"),
    };
}
