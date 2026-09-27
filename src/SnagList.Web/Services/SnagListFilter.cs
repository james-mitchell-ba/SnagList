namespace SnagList.Web.Services;

using SnagList.Domain.Snags;

public sealed record SnagListFilter(
    Guid? LocationId = null, SnagCategory? Category = null, SnagSeverity? Severity = null,
    SnagStatus? Status = null, string? Cursor = null, int Limit = 20)
{
    public string ToQueryString()
    {
        var parts = new List<string> { $"limit={Limit}" };
        if (LocationId is { } locationId) parts.Add($"locationId={locationId}");
        if (Category is { } category) parts.Add($"category={category}");
        if (Severity is { } severity) parts.Add($"severity={severity}");
        if (Status is { } status) parts.Add($"status={status}");
        if (Cursor is { } cursor) parts.Add($"cursor={Uri.EscapeDataString(cursor)}");
        return "?" + string.Join('&', parts);
    }
}
