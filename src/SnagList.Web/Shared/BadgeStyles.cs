using SnagList.Domain.Snags;

namespace SnagList.Web.Shared;

internal static class BadgeStyles
{
    public static string ForStatus(SnagStatus status) => status switch
    {
        SnagStatus.Reported => "bg-secondary",
        SnagStatus.Acknowledged => "bg-info text-dark",
        SnagStatus.InProgress => "bg-primary",
        SnagStatus.Resolved => "bg-success",
        SnagStatus.Closed => "bg-dark",
        SnagStatus.Rejected => "bg-danger",
        SnagStatus.Withdrawn => "bg-secondary",
        _ => "bg-secondary",
    };

    public static string ForSeverity(SnagSeverity severity) => severity switch
    {
        SnagSeverity.Low => "bg-secondary",
        SnagSeverity.Medium => "bg-warning text-dark",
        SnagSeverity.High => "bg-severity-high",
        SnagSeverity.SafetyCritical => "bg-danger",
        _ => "bg-secondary",
    };
}
