namespace SnagList.Domain.Snags;

public sealed class SnagComment
{
    public Guid Id { get; private set; }
    public Guid SnagId { get; private set; }
    public string AuthorStaffId { get; private set; } = "";
    public string AuthorName { get; private set; } = "";
    public string Body { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    private SnagComment() { } // EF Core

    public SnagComment(Guid snagId, string authorStaffId, string authorName, string body, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        SnagId = snagId;
        AuthorStaffId = authorStaffId;
        AuthorName = authorName;
        Body = body;
        CreatedAt = createdAt;
    }
}
