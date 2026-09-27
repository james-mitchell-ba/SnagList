namespace SnagList.Domain.Staff;

public sealed class StaffIdentity
{
    public string StaffId { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public IReadOnlyList<StaffRole> Roles { get; private set; } = [];
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    private StaffIdentity() { } // EF Core

    public static StaffIdentity FirstSeen(
        string staffId, string name, string email, IReadOnlyList<StaffRole> roles, DateTimeOffset now) => new()
    {
        StaffId = staffId,
        Name = name,
        Email = email,
        Roles = roles,
        FirstSeenAt = now,
        LastSeenAt = now,
    };

    public void Sync(string name, string email, IReadOnlyList<StaffRole> roles, DateTimeOffset now)
    {
        Name = name;
        Email = email;
        Roles = roles;
        LastSeenAt = now;
    }
}
