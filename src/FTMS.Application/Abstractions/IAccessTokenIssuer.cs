namespace FTMS.Application.Abstractions;

/// <summary>A signed access token and when it stops being valid.</summary>
public sealed record AccessToken(string Value, DateTime ExpiresAtUtc)
{
    /// <summary>Seconds until expiry, which is what OAuth style clients expect to be told.</summary>
    public int ExpiresInSeconds => Math.Max(0, (int)(ExpiresAtUtc - DateTime.UtcNow).TotalSeconds);
}

/// <summary>
/// Mints the short lived bearer token. The Application layer knows a token is a string with an
/// expiry; that it is a JWT signed with HMAC-SHA256 is Infrastructure's business, and swapping
/// to asymmetric signing or to a reference token should not touch a single handler.
/// design: doc 03 section 1, doc 06 section 3.
/// </summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(AuthenticatedUser user);
}
