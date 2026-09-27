namespace SnagList.Domain.Locations;

public sealed class Location
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Address { get; private set; } = "";
    public bool IsActive { get; private set; }

    private Location() { } // EF Core

    public static Location Create(string name, string address) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Address = address,
        IsActive = true,
    };

    public void Update(string name, string address)
    {
        Name = name;
        Address = address;
    }

    public void Retire() => IsActive = false;
}
