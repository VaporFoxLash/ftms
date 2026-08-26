using FTMS.Domain.Transactions;
using FTMS.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FTMS.Infrastructure.Persistence.Configurations;

internal sealed class TransactionAuditConfiguration : IEntityTypeConfiguration<TransactionAudit>
{
    public void Configure(EntityTypeBuilder<TransactionAudit> builder)
    {
        builder.ToTable("TransactionAudits");

        builder.HasKey(audit => audit.Id).IsClustered(false);

        builder.Property(audit => audit.Id).ValueGeneratedNever();

        builder.Property(audit => audit.TransactionId).IsRequired();

        builder.Property(audit => audit.ChangeType)
            .HasMaxLength(AuditChangeTypes.MaxLength)
            .IsRequired();

        builder.Property(audit => audit.OldValues);

        builder.Property(audit => audit.NewValues).IsRequired();

        builder.Property(audit => audit.ChangedBy)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(audit => audit.ChangedAtUtc)
            .HasColumnType("datetime2(3)")
            .IsRequired();

        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(audit => audit.TransactionId)
            .HasConstraintName("FK_TransactionAudits_Transactions")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(audit => audit.DomainEvents);

        // design: doc 02 section 3 - the audit read path is always "everything that happened
        // to this transaction, in order", so the index leads with TransactionId. Clustering on
        // it also keeps one transaction's history physically together.
        builder.HasIndex(audit => new { audit.TransactionId, audit.ChangedAtUtc })
            .HasDatabaseName("IX_TransactionAudits_TransactionId")
            .IsClustered();
    }
}
