namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
