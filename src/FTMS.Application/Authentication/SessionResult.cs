namespace FTMS.Application.Authentication;

/// <summary>
/// Everything a successful sign in or refresh produces.
///
/// Note that <see cref="RefreshToken"/> travels through here but must NOT be serialised into the
/// response body. The API layer lifts it out into an httpOnly cookie and maps the rest onto a
/// separate response contract. Keeping the two in one record until the very last moment is
/// deliberate - the handler produces one atomic outcome, and exactly one place decides how each
/// half reaches the client. design: doc 06 section 3.
/// </summary>
/// <param name="AccessToken">Short lived bearer token. Held in memory by the SPA.</param>
/// <param name="ExpiresInSeconds">Lifetime of the access token.</param>
/// <param name="UserName">Sign in name, and what the audit trail records.</param>
/// <param name="DisplayName">Shown in the UI.</param>
/// <param name="Roles">Role names, so the client can hide what the server would refuse anyway.</param>
/// <param name="RefreshToken">Raw refresh token. Cookie only. Never a response body, never a log.</param>
/// <param name="RefreshExpiresAtUtc">Cookie expiry.</param>
public sealed record SessionResult(
    string AccessToken,
    int ExpiresInSeconds,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string RefreshToken,
    DateTime RefreshExpiresAtUtc);

/// <summary>The authenticated caller, for the SPA to rehydrate its shell after a reload.</summary>
public sealed record CurrentUserDto(string UserName, string DisplayName, IReadOnlyList<string> Roles);
