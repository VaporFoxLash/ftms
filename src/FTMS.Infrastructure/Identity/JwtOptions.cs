namespace FTMS.Infrastructure.Identity;

/// <summary>
/// Token settings, bound from the <c>Jwt</c> configuration section.
///
/// design: doc 06 section 3. Every property here is validated by AddFtmsIdentity with
/// ValidateOnStart, so a deployment with a missing or weak signing key fails immediately and
/// visibly instead of at three in the morning when the first person tries to sign in. The rules
/// live there rather than as attributes here so the assembly does not take a dependency on
/// Microsoft.Extensions.Options.DataAnnotations for four checks.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HMAC-SHA256 keys shorter than the 256 bit hash gain nothing and lose security margin, so
    /// 32 bytes is the floor rather than a suggestion. Microsoft.IdentityModel throws below it
    /// anyway; failing here gives a message that says what to do about it.
    /// </summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>
    /// The key committed to appsettings.Development.json. Named here so the startup guard can
    /// reject it by exact value outside Development, rather than by guessing at substrings.
    /// </summary>
    public const string KnownDevelopmentKey =
        "ftms-development-only-signing-key-do-not-use-anywhere-real-32b+";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Read from a secret store in any real deployment, never from a committed file.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Short by design, and capped at an hour by the startup validation. The access token cannot
    /// be revoked - that is what makes it cheap to validate - so its lifetime IS the blast radius
    /// of a stolen one. Fifteen minutes is the documented figure. design: doc 06 section 3.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// How long a session can be renewed for without re-entering a password. Long, because the
    /// refresh token IS revocable server side, so its risk profile is nothing like the access
    /// token's.
    /// </summary>
    public int RefreshTokenDays { get; set; } = 14;
}
