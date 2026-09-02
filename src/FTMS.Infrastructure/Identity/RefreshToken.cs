namespace FTMS.Infrastructure.Identity;

/// <summary>
/// A single issued refresh token. Ours rather than Identity's, because Identity's AspNetUserTokens
/// is a key/value bag with no expiry, no rotation chain and no revocation semantics, and all
/// three are requirements here. design: doc 06 section 3.
///
/// The raw token value is NEVER stored. A refresh token is a bearer credential: anyone holding it
/// can mint access tokens until it expires, so a leaked database backup must not hand over live
/// sessions. Only <see cref="TokenHash"/>, a SHA-256 of the raw value, is persisted - the same
/// discipline a password column gets, minus the salt, because the raw value is already 256 bits
/// of cryptographic randomness and therefore not brute forceable from its digest.
///
/// Tokens are one time use. Presenting one sets <see cref="UsedAtUtc"/> and issues a successor,
/// linked through <see cref="ReplacedByTokenId"/>. Presenting an already used token means the
/// value leaked - the legitimate holder and an attacker now both have it - so the whole chain is
/// revoked rather than just that one row. See RefreshTokenStore.RotateAsync.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
        // EF materialisation.
    }

    /// <summary>
    /// The id is supplied rather than generated so a rotation can name its successor in the same
    /// atomic UPDATE that burns the predecessor - the chain link has to be written before the row
    /// it points at exists. See RefreshTokenStore.RotateAsync.
    /// </summary>
    public RefreshToken(Guid id, Guid userId, string tokenHash, DateTime expiresAtUtc, string? createdByIp)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedByIp = createdByIp;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 hex of the raw token. Unique, and the only lookup key.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Sized for IPv6. Kept for incident response, not for authorization decisions.</summary>
    public string? CreatedByIp { get; private set; }

    /// <summary>Set the first time this token is redeemed. A second redemption is an attack signal.</summary>
    public DateTime? UsedAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>The successor issued when this token was rotated. Forms the chain.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    public FtmsUser? User { get; private set; }

    /// <summary>
    /// Usable exactly once, and only while unexpired and unrevoked. The clock is passed in rather
    /// than read from <c>DateTime.UtcNow</c> so the expiry boundary is testable.
    /// </summary>
    public bool IsActiveAt(DateTime utcNow) =>
        UsedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > utcNow;

    public void MarkUsed(DateTime utcNow, Guid replacedBy)
    {
        UsedAtUtc = utcNow;
        ReplacedByTokenId = replacedBy;
    }

    public void Revoke(DateTime utcNow)
    {
        // Idempotent: revoking an already revoked token keeps the first timestamp, because that
        // is when the session actually ended.
        RevokedAtUtc ??= utcNow;
    }
}
