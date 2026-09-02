using FTMS.SharedKernel.Constants;

namespace FTMS.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The accounts IdentitySeeder creates when the test host starts.
///
/// Defined here and injected as configuration rather than borrowed from the API's
/// appsettings.Development.json, so the suite owns its own credentials. Reusing the shipped demo
/// accounts would mean a developer changing a demo password broke the test suite, and would make
/// it ambiguous whether a login failure was a bug or a configuration edit.
///
/// design: doc 08 section 3 - nothing is mocked below the HTTP boundary. These tests sign in
/// through the real endpoint against real PBKDF2 hashes, which is the only way to prove the
/// login actually works. TestTokens still exists alongside this for the authorization matrix,
/// where minting a token directly is the point.
/// </summary>
public static class TestUsers
{
    /// <summary>Meets the 12 character, upper, lower and digit policy from AddFtmsIdentity.</summary>
    public const string Password = "IntegrationTest#2026";

    public const string Capturer = "it.capturer";
    public const string Manager = "it.manager";
    public const string Auditor = "it.auditor";
    public const string Admin = "it.admin";

    /// <summary>
    /// Reserved for the lockout test and used by nothing else.
    ///
    /// Respawn does not reset AspNetUsers between tests, so AccessFailedCount and LockoutEnd
    /// survive - a lockout test that used a shared account would lock it for every test that ran
    /// afterwards, and the failures would look like an authorization bug.
    /// </summary>
    public const string Lockout = "it.lockout";

    private static readonly (string UserName, string Role)[] All =
    [
        (Capturer, FtmsRoles.Capturer),
        (Manager, FtmsRoles.Manager),
        (Auditor, FtmsRoles.Auditor),
        (Admin, FtmsRoles.Admin),
        (Lockout, FtmsRoles.Capturer),
    ];

    /// <summary>
    /// Flattens the accounts into the colon delimited keys the configuration binder expects.
    /// Indices start at zero so these OVERRIDE the demo accounts in the API's own
    /// appsettings.Development.json rather than appending to them.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string?>> SeedConfiguration() =>
        All.SelectMany((user, index) => new KeyValuePair<string, string?>[]
        {
            new($"Identity:SeedUsers:{index}:UserName", user.UserName),
            new($"Identity:SeedUsers:{index}:Password", Password),
            new($"Identity:SeedUsers:{index}:Role", user.Role),
            new($"Identity:SeedUsers:{index}:DisplayName", user.UserName),
        });
}
