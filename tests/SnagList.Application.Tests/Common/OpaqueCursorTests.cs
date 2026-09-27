namespace SnagList.Application.Tests.Common;

using SnagList.Application.Common;
using Xunit;

public class OpaqueCursorTests
{
    private sealed record Key(string Name, Guid Id);

    [Fact]
    public void Decode_of_Encode_round_trips()
    {
        var key = new Key("Head Office", Guid.NewGuid());

        var encoded = OpaqueCursor.Encode(key);
        var decoded = OpaqueCursor.Decode<Key>(encoded);

        Assert.Equal(key, decoded);
    }

    [Fact]
    public void Encoded_cursor_contains_no_padding_or_url_unsafe_characters()
    {
        var encoded = OpaqueCursor.Encode(new Key("Head Office", Guid.NewGuid()));

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Fact]
    public void Decode_of_null_or_empty_returns_default()
    {
        Assert.Null(OpaqueCursor.Decode<Key>(null));
        Assert.Null(OpaqueCursor.Decode<Key>(""));
    }
}
