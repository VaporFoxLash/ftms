using FTMS.Infrastructure.Identity;
using FTMS.SharedKernel.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FTMS.Infrastructure.Persistence.Configurations;

internal sealed class FtmsRoleConfiguration : IEntityTypeConfiguration<FtmsRole>
{
    public void Configure(EntityTypeBuilder<FtmsRole> builder)
    {
        // design: doc 06 section 3 - the four roles are seeded, not created at runtime. They are
        // part of the schema's meaning, the same way the five transaction statuses are: an
        // authorization policy names them at startup, so a missing role is a broken deployment
        // rather than a data entry task.
        //
        // Generated from FtmsRoles, so the table and the constants the policies are built from
        // cannot drift apart. Users are NOT seeded here - a password hash cannot be produced in
        // a migration - see IdentitySeeder.
        builder.HasData(FtmsRoles.All.Select(name =>
            new FtmsRole(FtmsRoleIds.ByName[name], name)));
    }
}
