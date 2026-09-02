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
    /// <summary>
    /// A SQL Server rowversion is always 8 bytes, which is 12 base64 characters. The cap is
    /// generous enough to absorb padding and any future widening, and small enough that the
    /// stack allocation below is bounded by this constant rather than by a request header.
    /// </summary>
    private const int MaxEncodedLength = 64;

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

        // Length is checked BEFORE the stackalloc below, and that ordering is the whole point.
        // The buffer used to be sized from candidate.Length, which is attacker controlled: a
        // multi megabyte If-Match header would have tried to allocate a multi megabyte buffer on
        // a 1MB thread stack, and a stack overflow cannot be caught - it terminates the process.
        // A valid value is twelve characters, so anything longer is malformed regardless.
        if (candidate.Length is 0 or > MaxEncodedLength)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[MaxEncodedLength];
        if (!Convert.TryFromBase64String(candidate, buffer, out var written) || written == 0)
        {
            return false;
        }

        rowVersion = buffer[..written].ToArray();

        return true;
    }
}
