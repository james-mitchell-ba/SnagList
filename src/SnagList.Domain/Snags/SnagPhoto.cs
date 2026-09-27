namespace SnagList.Domain.Snags;

public sealed record SnagPhoto(
    string BlobKey, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);
