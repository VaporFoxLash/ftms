using Microsoft.AspNetCore.Identity;

namespace FTMS.Infrastructure.Identity;

/// <summary>
/// A role, keyed by <see cref="Guid"/> to match <see cref="FtmsUser"/>.
///
/// The four role names are defined once in <see cref="FTMS.SharedKernel.Constants.FtmsRoles"/>
/// and their ids in <see cref="FTMS.SharedKernel.Constants.FtmsRoleIds"/>; this type is the
/// persistence shape of those constants and must never invent a fifth.
/// design: doc 06 section 3.
/// </summary>
public sealed class FtmsRole : IdentityRole<Guid>
{
    public FtmsRole()
    {
    }

    public FtmsRole(Guid id, string name)
    {
        Id = id;
        Name = name;
        NormalizedName = name.ToUpperInvariant();

        // Fixed rather than generated, for the same reason the id is: HasData seeding has to
        // produce byte identical rows on every run or EF emits a spurious migration.
        ConcurrencyStamp = id.ToString();
    }
}
