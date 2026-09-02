namespace FTMS.Api.Contracts;

/// <summary>
/// design: doc 06 section 3 - a username and a password, and nothing else. In particular there
/// is no role field: the previous development stub let the caller name the roles they wanted,
/// which is exactly the property that made it a stub rather than a login. Roles come from the
/// identity store.
/// </summary>
public sealed record LoginRequest(string UserName, string Password);

/// <summary>
/// What a successful sign in or refresh returns.
///
/// Note what is NOT here: the refresh token. It leaves in a Set-Cookie header marked HttpOnly,
/// so script cannot read it and an XSS cannot exfiltrate it. Putting it in this body would undo
/// that in one line. design: doc 06 section 3.
/// </summary>
/// <param name="AccessToken">Bearer token for the Authorization header. Hold in memory only.</param>
/// <param name="ExpiresInSeconds">Access token lifetime, so the client can refresh ahead of expiry.</param>
/// <param name="UserName">Sign in name.</param>
/// <param name="DisplayName">Name to show in the UI.</param>
/// <param name="Roles">
/// So the client can hide controls the server would refuse anyway. This is a usability
/// affordance and never a security control - every endpoint re-checks the policy regardless.
/// </param>
public sealed record SessionResponse(
    string AccessToken,
    int ExpiresInSeconds,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles);

/// <summary>The authenticated caller, for rehydrating the SPA shell after a page reload.</summary>
public sealed record CurrentUserResponse(
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles);
