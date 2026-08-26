namespace FTMS.Application.Transactions;

/// <summary>
/// Converts between the SQL Server rowversion and the HTTP ETag that carries it.
///
/// design: doc 02 section 1.8 and doc 05 section 6 - rowversion is the concurrency token,
/// surfaced to clients as an ETag with If-Match semantics so silent last writer wins cannot
/// happen on financial records. Lives in Application because both sides need it: Infrastructure
/// formats one when reading, the API parses one from the If-Match header when writing.
/// </summary>
public static class ETag
{
    /// <summary>Formats a rowversion as a strong ETag, quotes included, per RFC 9110.</summary>
    public static string From(byte[]? rowVersion) =>
        rowVersion is null || rowVersion.Length == 0
            ? "\"\""
            : $"\"{Convert.ToBase64String(rowVersion)}\"";

    /// <summary>
    /// Parses an If-Match header value back into a rowversion. Tolerates the quotes an HTTP
    /// client is required to send and the weak validator prefix some proxies add, and returns
    /// false rather than throwing on anything it does not understand, because a malformed
    /// header is a client error, not an exception.
    /// </summary>
    public static bool TryParse(string? headerValue, out byte[] rowVersion)
    {
        rowVersion = [];

        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var candidate = headerValue.Trim();

        if (candidate.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[2..];
        }

        candidate = candidate.Trim('"');

        if (candidate.Length == 0)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[candidate.Length];
        if (!Convert.TryFromBase64String(candidate, buffer, out var written) || written == 0)
        {
            return false;
        }

        rowVersion = buffer[..written].ToArray();

        return true;
    }
}
