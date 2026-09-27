namespace SnagList.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
