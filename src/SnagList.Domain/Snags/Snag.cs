namespace SnagList.Domain.Snags;

using SnagList.Domain.Snags.Events;

public sealed class Snag
{
    private const int MaxPhotos = 5;

    private static readonly Dictionary<SnagStatus, SnagStatus[]> AllowedTransitions = new()
    {
        [SnagStatus.Reported] = [SnagStatus.Acknowledged, SnagStatus.Rejected, SnagStatus.Withdrawn],
        [SnagStatus.Acknowledged] = [SnagStatus.InProgress, SnagStatus.Rejected],
        [SnagStatus.InProgress] = [SnagStatus.Resolved],
        [SnagStatus.Resolved] = [SnagStatus.Closed],
        [SnagStatus.Closed] = [],
        [SnagStatus.Rejected] = [],
        [SnagStatus.Withdrawn] = [],
    };

    private readonly List<SnagComment> _comments = [];
    private readonly List<SnagPhoto> _photos = [];
    private readonly List<object> _domainEvents = [];

    public Guid Id { get; private set; }
    public Guid LocationId { get; private set; }
    public string SubLocation { get; private set; } = "";
    public SnagCategory Category { get; private set; }
    public SnagSeverity Severity { get; private set; }
    public string Description { get; private set; } = "";
    public SnagStatus Status { get; private set; }
    public string ReportedByStaffId { get; private set; } = "";
    public string ReportedByName { get; private set; } = "";
    public DateTimeOffset ReportedAt { get; private set; }
    public int Version { get; private set; }

    public IReadOnlyList<SnagComment> Comments => _comments;
    public IReadOnlyList<SnagPhoto> Photos => _photos;
    public IReadOnlyList<object> DomainEvents => _domainEvents;

    private Snag() { } // EF Core

    public static Snag Report(
        Guid locationId, string subLocation, SnagCategory category, SnagSeverity severity,
        string description, string reportedByStaffId, string reportedByName, DateTimeOffset reportedAt)
    {
        var snag = new Snag
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            SubLocation = subLocation,
            Category = category,
            Severity = severity,
            Description = description,
            Status = SnagStatus.Reported,
            ReportedByStaffId = reportedByStaffId,
            ReportedByName = reportedByName,
            ReportedAt = reportedAt,
            Version = 1,
        };
        snag._domainEvents.Add(new SnagReported(snag.Id, locationId, severity, reportedByStaffId, reportedAt));
        return snag;
    }

    public void Edit(string subLocation, SnagCategory category, SnagSeverity severity, string description)
    {
        EnsureEditable();
        SubLocation = subLocation;
        Category = category;
        Severity = severity;
        Description = description;
        Version++;
    }

    public void AddPhoto(SnagPhoto photo)
    {
        EnsureEditable();
        if (_photos.Count >= MaxPhotos) throw new SnagPhotoLimitExceededException();
        _photos.Add(photo);
        Version++;
    }

    public void AddComment(string authorStaffId, string authorName, string body, DateTimeOffset createdAt)
    {
        _comments.Add(new SnagComment(Id, authorStaffId, authorName, body, createdAt));
        Version++;
    }

    public void TransitionTo(SnagStatus newStatus, string changedByStaffId, DateTimeOffset changedAt)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
        {
            throw new InvalidSnagStatusTransitionException(Status, newStatus);
        }

        var previous = Status;
        Status = newStatus;
        Version++;
        _domainEvents.Add(new SnagStatusChanged(Id, previous, newStatus, changedByStaffId, ReportedByStaffId, changedAt));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void EnsureEditable()
    {
        if (Status != SnagStatus.Reported) throw new SnagNotEditableException(Status);
    }
}
