using FTMS.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FTMS.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <summary>SHA-256 rendered as lower case hex is always exactly 64 characters.</summary>
    internal const int TokenHashLength = 64;

    /// <summary>Wide enough for a full IPv6 address, including an IPv4 mapped form.</summary>
    private const int IpAddressLength = 45;

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        // Nonclustered for the same reason Transactions is: SQL Server orders uniqueidentifier by
        // the last six bytes, so clustering on a GUID - even a sequential one - fragments the
        // table. The clustered index goes on the access path below instead.
        // design: doc 07 section 2.
        builder.HasKey(token => token.Id).IsClustered(false);

        builder.Property(token => token.Id).ValueGeneratedNever();

        // char, not nvarchar: the value is fixed length lower case hex, so a variable width
        // Unicode column would double the storage and the index depth for no benefit.
        builder.Property(token => token.TokenHash)
            .HasColumnType($"char({TokenHashLength})")
            .IsRequired();

        // The only lookup path. Unique because a hash collision here would mean two sessions
        // share a credential, and because it makes replay detection a single seek.
        builder.HasIndex(token => token.TokenHash)
            .HasDatabaseName("UQ_RefreshTokens_TokenHash")
            .IsUnique();

        builder.Property(token => token.CreatedByIp)
            .HasMaxLength(IpAddressLength);

        builder.Property(token => token.ExpiresAtUtc)
            .HasColumnType("datetime2(3)")
            .HasConversion(UtcDateTimeConverters.Utc)
            .IsRequired();

        builder.Property(token => token.CreatedAtUtc)
            .HasColumnType("datetime2(3)")
            .HasConversion(UtcDateTimeConverters.Utc)
            .IsRequired();

        builder.Property(token => token.UsedAtUtc)
            .HasColumnType("datetime2(3)")
            .HasConversion(UtcDateTimeConverters.NullableUtc);

        builder.Property(token => token.RevokedAtUtc)
            .HasColumnType("datetime2(3)")
            .HasConversion(UtcDateTimeConverters.NullableUtc);

        // Cascade, unlike every foreign key on the transaction side. Deleting a user should take
        // their sessions with them; a transaction, by contrast, is a financial record that must
        // survive whatever happens to the person who captured it. design: doc 02 section 3.
        builder.HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .HasConstraintName("FK_RefreshTokens_AspNetUsers")
            .OnDelete(DeleteBehavior.Cascade);

        // Clustered on the revocation path: "every token for this user, newest first" is what
        // RevokeChainAsync and the logout sweep both run.
        builder.HasIndex(token => new { token.UserId, token.CreatedAtUtc })
            .HasDatabaseName("IX_RefreshTokens_UserId_CreatedAtUtc")
            .IsClustered();
    }
}
