namespace SnagList.Api.Contracts;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, string? NextCursor);
