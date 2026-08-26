using FTMS.Application.Abstractions;

namespace FTMS.Infrastructure.Identity;

/// <summary>
/// Placeholder identity for the audit trail until real authentication lands.
///
/// TODO design: doc 06 section 3 - replace with an implementation that reads the authenticated
/// principal from IHttpContextAccessor. Doc 06 requires ASP.NET Core Identity self hosted,
/// 15 minute JWTs with rotating one time refresh tokens, TOTP MFA mandatory for privileged
/// roles, and four roles with separation of duty (Capturer, Manager, Auditor, Admin). Until
/// that is built, every audit row is stamped "system", which is honest about what we know
/// rather than inventing a user.
/// </summary>
internal sealed class SystemCurrentUser : ICurrentUser
{
    public const string SystemIdentity = "system";

    public string UserName => SystemIdentity;
}
