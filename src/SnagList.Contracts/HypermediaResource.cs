namespace SnagList.Contracts;

using System.Text.Json.Serialization;

public abstract class HypermediaResource
{
    [JsonPropertyName("_links")]
    public required IReadOnlyDictionary<string, ApiLink> Links { get; init; }
}
