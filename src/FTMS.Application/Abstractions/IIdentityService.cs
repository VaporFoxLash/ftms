namespace FTMS.Application.Abstractions;

/// <summary>
/// The outcome of checking a username and password.
///
/// A discriminated result rather than a bool, because the three failure modes need different
/// handling and collapsing them would either leak information or lose it: an unknown user and a
/// wrong password must be indistinguishable to the caller of the API, while a lockout must be
/// reported so the person is told to wait rather than to retype.
/// design: doc 06 section 3.
/// </summary>
public enum CredentialCheck
{
    /// <summary>Username and password both matched, and the account is usable.</summary>
    Succeeded = 0,

    /// <summary>No such user, or the password did not match. The two are not distinguished.</summary>
    Failed = 1,

    /// <summary>The account is locked out after repeated failures.</summary>
    LockedOut = 2,
}

/// <summary>An authenticated user, reduced to what a token needs.</summary>
/// <param name="UserId">The identity store's primary key.</param>
/// <param name="UserName">The sign in name, and what the audit trail records.</param>
/// <param name="DisplayName">Shown in the UI.</param>
/// <param name="Roles">Role names, drawn from FtmsRoles.</param>
public sealed record AuthenticatedUser(
    Guid UserId,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles);

/// <summary>
/// Credential verification and role lookup, without the Application layer knowing that ASP.NET
/// Core Identity, PBKDF2 or a database exist. design: doc 03 section 1 - Application declares
/// what it needs; Infrastructure supplies it.
/// </summary>
public interface IIdentityService
{
    /// <summary>
    /// Verifies a password, honouring lockout. Implementations must consume a failed attempt
    /// against the lockout counter, and must take a comparable amount of time whether or not the
    /// user exists, so the endpoint cannot be used to enumerate accounts.
    /// </summary>
    Task<CredentialCheck> CheckCredentialsAsync(
        string userName,
        string password,
        CancellationToken cancellationToken);

    /// <summary>
    /// The user and their roles, or null when no such user exists. Called after a successful
    /// credential check, and on refresh to pick up role changes without waiting for the session
    /// to end.
    /// </summary>
    Task<AuthenticatedUser?> FindByNameAsync(string userName, CancellationToken cancellationToken);

    /// <summary>The user behind an already validated refresh token.</summary>
    Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
}
