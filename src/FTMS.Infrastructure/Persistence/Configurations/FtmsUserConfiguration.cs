using FTMS.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FTMS.Infrastructure.Persistence.Configurations;

internal sealed class FtmsUserConfiguration : IEntityTypeConfiguration<FtmsUser>
{
    /// <summary>
    /// Matches TransactionAudits.ChangedBy, which is where this value ends up. Keeping the two
    /// equal means a display name can never be silently truncated on its way into the audit
    /// trail. design: doc 02 section 1.7.
    /// </summary>
    internal const int DisplayNameMaxLength = 100;

    public void Configure(EntityTypeBuilder<FtmsUser> builder)
    {
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.DisplayName)
            .HasMaxLength(DisplayNameMaxLength)
            .IsRequired();

        builder.Property(user => user.CreatedAtUtc)
            .HasColumnType("datetime2(3)")
            .HasConversion(UtcDateTimeConverters.Utc)
            .IsRequired();
    }
}
