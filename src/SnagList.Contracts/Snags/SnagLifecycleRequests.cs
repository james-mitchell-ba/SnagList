namespace SnagList.Contracts.Snags;

public sealed record ChangeSnagStatusRequest(int ExpectedVersion);
public sealed record RejectSnagRequest(string Reason, int ExpectedVersion);
public sealed record AddSnagCommentRequest(string Body);
