namespace FTMS.Infrastructure.Identity;

/// <summary>
/// One demo account to create on startup.
///
/// design: doc 06 section 3 - seeded accounts exist so the stack is usable the moment it starts,
/// and so the authorization matrix can be exercised by hand against four real sign ins rather
/// than four hand crafted tokens. The seeder refuses to run outside Development, because a known
/// username and password committed to a configuration file is a backdoor anywhere else.
/// </summary>
public sealed class SeedUserOptions
{
    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Must be one of FtmsRoles.All. IdentitySeeder throws on anything else.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Falls back to <see cref="UserName"/> when blank.</summary>
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>Bound from the <c>Identity</c> configuration section.</summary>
public sealed class IdentitySeedOptions
{
    public const string SectionName = "Identity";

    public List<SeedUserOptions> SeedUsers { get; set; } = [];
}
