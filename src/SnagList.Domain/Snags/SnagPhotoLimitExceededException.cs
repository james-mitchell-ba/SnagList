namespace SnagList.Domain.Snags;

public sealed class SnagPhotoLimitExceededException : Exception
{
    public SnagPhotoLimitExceededException() : base("A Snag may carry at most 5 photos.") { }
}
