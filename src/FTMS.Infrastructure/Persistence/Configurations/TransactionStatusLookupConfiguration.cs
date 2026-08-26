using FTMS.Domain.Transactions;
using FTMS.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FTMS.Infrastructure.Persistence.Configurations;

internal sealed class TransactionStatusLookupConfiguration : IEntityTypeConfiguration<TransactionStatusLookup>
{
    public void Configure(EntityTypeBuilder<TransactionStatusLookup> builder)
    {
        builder.ToTable("TransactionStatuses");

        builder.HasKey(status => status.Id);

        builder.Property(status => status.Id).ValueGeneratedNever();

        builder.Property(status => status.StatusName)
            .HasMaxLength(TransactionStatus.MaxNameLength)
            .IsRequired();

        builder.HasIndex(status => status.StatusName)
            .HasDatabaseName("UQ_TransactionStatuses_StatusName")
            .IsUnique();

        // design: doc 02 section 4 - seeded through HasData with fixed GUIDs, not NEWID().
        // Migrations must be deterministic so every environment gets identical rows, and the
        // application references well known status ids as constants without a lookup on every
        // request. The rows are generated from the smart enum, so the table and the domain
        // cannot disagree about what the five statuses are.
        builder.HasData(TransactionStatus.List.Select(status =>
            new TransactionStatusLookup(status.Value, status.Name)));
    }
}
