using FTMS.SharedKernel.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FTMS.Infrastructure.Identity;

/// <summary>
/// Creates the demo accounts described in configuration, if they are not already there.
///
/// Users are seeded here rather than through <c>HasData</c> in a migration, because a migration
/// cannot produce a password hash: Identity's hasher salts every hash, so the value differs on
/// every run and EF would see a permanent model difference. Roles, which have no such problem,
/// ARE seeded in the migration - see FtmsRoleConfiguration.
///
/// design: doc 06 section 3.
/// </summary>
public static class IdentitySeeder
{
    /// <summary>
    /// Idempotent: existing users are left exactly as they are, including their password. Running
    /// this on every startup must never reset a password a developer has deliberately changed.
    /// </summary>
    /// <param name="services">A scoped provider. UserManager is scoped.</param>
    /// <param name="isDevelopment">
    /// Seeding is refused outside Development. The credentials live in a committed configuration
    /// file, so creating these accounts in any other environment would be provisioning a known
    /// password into a real system.
    /// </param>
    public static async Task SeedAsync(
        IServiceProvider services,
        bool isDevelopment,
        CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        if (!isDevelopment)
        {
            logger.LogInformation(
                "Identity seeding skipped: the seeded credentials are development only.");
            return;
        }

        var options = services.GetRequiredService<IOptions<IdentitySeedOptions>>().Value;
        if (options.SeedUsers.Count == 0)
        {
            return;
        }

        var users = services.GetRequiredService<UserManager<FtmsUser>>();

        foreach (var seed in options.SeedUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!FtmsRoles.All.Contains(seed.Role))
            {
                // Loud, because a typo here silently produces an account that can do nothing and
                // a developer who cannot work out why.
                throw new InvalidOperationException(
                    $"Seed user '{seed.UserName}' asks for role '{seed.Role}', which is not one of "
                    + $"{string.Join(", ", FtmsRoles.All)}.");
            }

            if (await users.FindByNameAsync(seed.UserName) is not null)
            {
                continue;
            }

            var user = new FtmsUser(seed.UserName)
            {
                DisplayName = string.IsNullOrWhiteSpace(seed.DisplayName) ? seed.UserName : seed.DisplayName,
                Email = $"{seed.UserName}@ftms.local",
                EmailConfirmed = true,
            };

            var created = await users.CreateAsync(user, seed.Password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not seed user '{seed.UserName}': "
                    + string.Join("; ", created.Errors.Select(error => error.Description)));
            }

            var assigned = await users.AddToRoleAsync(user, seed.Role);
            if (!assigned.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not assign role '{seed.Role}' to '{seed.UserName}': "
                    + string.Join("; ", assigned.Errors.Select(error => error.Description)));
            }

            logger.LogInformation(
                "Seeded development user {UserName} in role {Role}.",
                seed.UserName,
                seed.Role);
        }
    }
}
