namespace FTMS.Application.Abstractions;

/// <summary>
/// A newly minted refresh token. The raw value exists only here, in flight, on its way into a
/// Set-Cookie header - the store persists nothing but its hash.
/// </summary>
/// <param name="RawToken">The opaque value handed to the client. Never logged, never stored.</param>
/// <param name="ExpiresAtUtc">When the cookie should expire.</param>
public sealed record IssuedRefreshToken(string RawToken, DateTime ExpiresAtUtc);

/// <summary>
/// Why a rotation attempt failed.
/// </summary>
public enum RefreshFailure
{
    /// <summary>No token with that hash. Expired and swept, or simply fabricated.</summary>
    Unknown = 0,

    /// <summary>Past its expiry.</summary>
    Expired = 1,

    /// <summary>Revoked by a logout, or by the replay response below.</summary>
    Revoked = 2,

    /// <summary>
    /// Already redeemed once. This is the interesting one: refresh tokens are single use, so a
    /// second presentation means the value is held by two parties and one of them is an attacker.
    /// The implementation revokes the entire chain in response, ending both sessions - the
    /// legitimate user is inconvenienced into signing in again, which is the correct trade.
    /// design: doc 06 section 3.
    /// </summary>
    Replayed = 3,
}

/// <summary>The result of presenting a refresh token.</summary>
/// <param name="UserId">Set only when <paramref name="Failure"/> is null.</param>
/// <param name="Replacement">The successor token, set only on success.</param>
/// <param name="Failure">Null on success.</param>
public sealed record RefreshResult(
    Guid UserId,
    IssuedRefreshToken? Replacement,
    RefreshFailure? Failure)
{
    public bool Succeeded => Failure is null;

    public static RefreshResult Success(Guid userId, IssuedRefreshToken replacement) =>
        new(userId, replacement, null);

    public static RefreshResult Rejected(RefreshFailure failure) =>
        new(Guid.Empty, null, failure);
}

/// <summary>
/// Issues, rotates and revokes refresh tokens. design: doc 06 section 3 - rotating, one time
/// use, and revocable server side, which is the whole reason this is a database table rather
/// than a second self contained JWT.
/// </summary>
public interface IRefreshTokenStore
{
    Task<IssuedRefreshToken> IssueAsync(Guid userId, string? clientIp, CancellationToken cancellationToken);

    /// <summary>
    /// Redeems a token and issues its successor in one unit of work. Detects replay and revokes
    /// the chain when it finds it.
    /// </summary>
    Task<RefreshResult> RotateAsync(string rawToken, string? clientIp, CancellationToken cancellationToken);

    /// <summary>Ends one session. Idempotent - revoking an unknown or already dead token succeeds.</summary>
    Task RevokeAsync(string rawToken, CancellationToken cancellationToken);

    /// <summary>Ends every live session for a user. Used on replay detection.</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken);
}
