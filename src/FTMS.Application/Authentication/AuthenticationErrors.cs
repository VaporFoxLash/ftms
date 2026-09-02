using FTMS.SharedKernel.Results;

namespace FTMS.Application.Authentication;

/// <summary>
/// Every authentication failure, named once. As with DomainErrors, these codes become the
/// ProblemDetails <c>type</c> URI suffix and are therefore part of the API contract.
///
/// design: doc 06 section 3 and doc 06 section 7 - note how little these messages say. An error
/// that distinguishes "no such user" from "wrong password" turns the login endpoint into an
/// account enumeration oracle, so both map to <see cref="InvalidCredentials"/> and the client
/// cannot tell which happened.
/// </summary>
public static class AuthenticationErrors
{
    /// <summary>Bad username, bad password, or both. Deliberately indistinguishable.</summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "auth.invalid_credentials",
        "The username or password is incorrect.");

    /// <summary>
    /// Only ever returned after the password itself has been checked, so it reveals nothing an
    /// attacker who already had the password would not know.
    /// </summary>
    public static readonly Error AccountLocked = Error.Locked(
        "auth.account_locked",
        "This account is temporarily locked after repeated failed sign in attempts. "
        + "Try again later, or ask an administrator to unlock it.");

    /// <summary>No cookie was presented, so there is nothing to rotate.</summary>
    public static readonly Error NoRefreshToken = Error.Unauthorized(
        "auth.no_refresh_token",
        "No session cookie was presented.");

    /// <summary>
    /// Unknown, expired, revoked or replayed. Collapsed into one message on purpose: telling a
    /// caller which of the four it was would confirm that a stolen token had once been valid.
    /// The replay case is handled loudly on the server - the whole chain is revoked - and
    /// quietly to the client.
    /// </summary>
    public static readonly Error InvalidRefreshToken = Error.Unauthorized(
        "auth.invalid_refresh_token",
        "Your session has expired. Please sign in again.");

    /// <summary>
    /// The token validated but the user behind it is gone or disabled. Rare, and worth its own
    /// code because it means a live token outlived its account.
    /// </summary>
    public static readonly Error UserNoLongerActive = Error.Unauthorized(
        "auth.user_no_longer_active",
        "This account is no longer active.");
}
