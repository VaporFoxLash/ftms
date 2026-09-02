using FTMS.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace FTMS.Infrastructure.Identity;

/// <summary>
/// Credential verification over ASP.NET Core Identity. design: doc 06 section 3.
///
/// Uses <see cref="UserManager{TUser}"/> directly rather than SignInManager. SignInManager would
/// give the same lockout behaviour, but it also drags in the cookie authentication scheme it
/// expects to sign into - and this API's default scheme is JWT bearer. The four lines of lockout
/// bookkeeping below are cheaper than reconciling two authentication schemes that never both
/// apply. No password hashing is hand rolled either way: <see cref="UserManager{TUser}"/> owns
/// that, and it is PBKDF2 with Identity's current iteration count.
/// </summary>
internal sealed class IdentityService(UserManager<FtmsUser> users) : IIdentityService
{
    /// <summary>
    /// A valid password hash of a value nobody knows, used to burn the same PBKDF2 work on an
    /// unknown username as on a known one.
    ///
    /// Without this, "no such user" returns in microseconds while "wrong password" takes the
    /// tens of milliseconds PBKDF2 costs, and the difference is measurable across a network. An
    /// attacker with a username list could then sort it into real and fake accounts without ever
    /// guessing a password. design: doc 06 section 7.
    /// </summary>
    private readonly Lazy<string> decoyHash = new(() =>
        users.PasswordHasher.HashPassword(new FtmsUser(), Guid.CreateVersion7().ToString()));

    public async Task<CredentialCheck> CheckCredentialsAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByNameAsync(userName);

        if (user is null)
        {
            // Deliberately still does the work. See decoyHash.
            users.PasswordHasher.VerifyHashedPassword(new FtmsUser(), decoyHash.Value, password);
            return CredentialCheck.Failed;
        }

        // Checked before the password, not after: a locked account must not have its password
        // tested at all, or the lockout becomes a rate limit an attacker can still probe through.
        if (await users.IsLockedOutAsync(user))
        {
            return CredentialCheck.LockedOut;
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            // Increments AccessFailedCount and sets LockoutEnd once it crosses the threshold
            // configured in AddInfrastructureIdentity.
            await users.AccessFailedAsync(user);

            // Re-read rather than assume: this attempt may have been the one that tripped it,
            // and the caller should be told to wait rather than to retype.
            return await users.IsLockedOutAsync(user)
                ? CredentialCheck.LockedOut
                : CredentialCheck.Failed;
        }

        // A correct password clears the counter. Otherwise four typos spread across a month
        // would eventually lock an account that was never under attack.
        if (await users.GetAccessFailedCountAsync(user) > 0)
        {
            await users.ResetAccessFailedCountAsync(user);
        }

        return CredentialCheck.Succeeded;
    }

    public async Task<AuthenticatedUser?> FindByNameAsync(string userName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByNameAsync(userName);

        return user is null ? null : await ProjectAsync(user);
    }

    public async Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByIdAsync(userId.ToString());

        return user is null ? null : await ProjectAsync(user);
    }

    private async Task<AuthenticatedUser> ProjectAsync(FtmsUser user)
    {
        var roles = await users.GetRolesAsync(user);

        return new AuthenticatedUser(
            user.Id,
            user.UserName ?? string.Empty,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName ?? string.Empty : user.DisplayName,
            [.. roles]);
    }
}
