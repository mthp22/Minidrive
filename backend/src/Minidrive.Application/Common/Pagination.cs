using System.Text;
using System.Text.Json;

namespace Minidrive.Application.Common;

/// <summary>Stable sort position of a node within a folder: ordered by name then id.</summary>
public sealed record NodeCursor(string Name, string Id);

/// <summary>A request for one page of results, optionally continuing after a cursor.</summary>
public sealed record PageRequest(int PageSize, NodeCursor? After);

/// <summary>A page of results plus the opaque continuation token for the next page.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    public bool HasMore => !string.IsNullOrEmpty(NextCursor);
}

/// <summary>
/// Encodes and decodes opaque pagination cursors. The token is base64url-encoded JSON so callers
/// cannot rely on its structure and so future ordering changes do not break old tokens.
/// </summary>
public static class CursorCodec
{
    private sealed record Payload(string Name, string Id);

    public static string Encode(NodeCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        var payload = new Payload(cursor.Name, cursor.Id);
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Base64UrlEncode(json);
    }

    /// <summary>
    /// Decodes a cursor. Returns <see langword="null"/> when the token is absent, malformed or
    /// carries no usable position, so a bad token yields an empty cursor rather than an error.
    /// </summary>
    public static NodeCursor? Decode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var bytes = Base64UrlDecode(token);
            var payload = JsonSerializer.Deserialize<Payload>(bytes);
            if (payload is null || string.IsNullOrEmpty(payload.Id))
            {
                return null;
            }

            return new NodeCursor(payload.Name ?? string.Empty, payload.Id);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var builder = new StringBuilder(value.Replace('-', '+').Replace('_', '/'));
        switch (builder.Length % 4)
        {
            case 2:
                builder.Append("==");
                break;
            case 3:
                builder.Append('=');
                break;
            default:
                break;
        }

        return Convert.FromBase64String(builder.ToString());
    }
}
