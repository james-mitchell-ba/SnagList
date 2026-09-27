namespace SnagList.Api.Contracts.Snags;

using SnagList.Domain.Snags;

public sealed record ReportSnagRequest(
    Guid LocationId, string SubLocation, SnagCategory Category, SnagSeverity Severity, string Description);

public sealed record EditSnagRequest(
    string SubLocation, SnagCategory Category, SnagSeverity Severity, string Description, int ExpectedVersion);

public sealed record WithdrawSnagRequest(int ExpectedVersion);
