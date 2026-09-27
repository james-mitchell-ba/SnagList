namespace SnagList.Application.Common;

using System.Text.Json;

public static class OpaqueCursor
{
    public static string Encode<T>(T key) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(key))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public static T? Decode<T>(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return default;

        var base64 = cursor.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - (base64.Length % 4)) % 4);
        return JsonSerializer.Deserialize<T>(Convert.FromBase64String(base64));
    }
}
