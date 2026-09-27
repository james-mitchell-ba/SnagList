namespace SnagList.Infrastructure.Clock;

using SnagList.Application.Abstractions;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
