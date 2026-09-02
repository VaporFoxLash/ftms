using Microsoft.AspNetCore.Identity;

namespace FTMS.Infrastructure.Identity;

/// <summary>
/// The application user. design: doc 06 section 3 - ASP.NET Core Identity self hosted, with its
/// tables in our own SQL Server rather than a third party directory.
///
/// Keyed by <see cref="Guid"/> rather than Identity's default string, so the identity tables
/// match the key type every other table in this database already uses. Ids are GUIDv7 for the
/// same reason transaction ids are: they sort by creation time, which keeps index inserts at the
/// right hand edge instead of scattering them.
///
/// Everything security sensitive - the password hash, the security stamp, the lockout counters -
/// is inherited from <see cref="IdentityUser{TKey}"/> and hashed by Identity's own PBKDF2
/// implementation. Nothing here hand rolls crypto, which is the entire point of using Identity.
/// </summary>
public sealed class FtmsUser : IdentityUser<Guid>
{
    public FtmsUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.CreateVersion7().ToString();
    }

    public FtmsUser(string userName)
        : this()
    {
        UserName = userName;
    }

    /// <summary>
    /// The name shown in the UI and written to the audit trail's ChangedBy column. Separate from
    /// <see cref="IdentityUser{TKey}.UserName"/> so a person can be renamed for display without
    /// invalidating their sign in credential.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
