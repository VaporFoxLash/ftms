using FTMS.Domain.Transactions;
using FTMS.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FTMS.Infrastructure.Persistence.Configurations;

/// <summary>design: doc 02 sections 2 and 3 - this is the reference DDL, expressed as code first.</summary>
internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable(
            "Transactions",
            table => table.HasCheckConstraint("CK_Transactions_Amount", "[Amount] >= 0"));

        // The primary key is NONCLUSTERED, which is a correction to doc 02 section 1.2 rather
        // than a contradiction of it.
        //
        // The doc reasons that sequential GUIDs "keep inserts appending near the end of the
        // index". That is true of GUID v7 in byte order, but SQL Server does not compare
        // uniqueidentifier in byte order: it compares the LAST six bytes first. GUID v7 puts
        // its timestamp in the FIRST six, so to SQL Server a v7 key still looks random and a
        // clustered PK on it would page split exactly as the doc feared. (This is the whole
        // reason NEWSEQUENTIALID exists.)
        //
        // So: keep the client generated v7 key the design asked for, make it nonclustered, and
        // cluster on CreatedAtUtc instead, which really does increase monotonically. The
        // design's intent is preserved; only its stated mechanism changes.
        builder.HasKey(transaction => transaction.Id).IsClustered(false);

        builder.Property(transaction => transaction.Id)
            .ValueGeneratedNever();

        builder.Property(transaction => transaction.TransactionDate)
            .HasColumnType("datetime2(3)")
            .IsRequired();

        // design: doc 02 section 1.5 - the column stays NVARCHAR(50) exactly as the brief
        // specifies; the smart enum keeps the values that reach it clean.
        builder.Property(transaction => transaction.Type)
            .HasConversion(
                type => type.Name,
                name => ParseType(name),
                new ValueComparer<TransactionType>(
                    (left, right) => left!.Equals(right),
                    type => type.GetHashCode(),
                    type => type))
            .HasColumnName("TransactionType")
            .HasMaxLength(TransactionType.MaxLength)
            .IsRequired();

        builder.Property(transaction => transaction.TransactionStatusId)
            .IsRequired();

        builder.HasOne<TransactionStatusLookup>()
            .WithMany()
            .HasForeignKey(transaction => transaction.TransactionStatusId)
            .HasConstraintName("FK_Transactions_TransactionStatuses")
            .OnDelete(DeleteBehavior.Restrict);

        // design: doc 02 section 1.1 - DECIMAL(18,2) and CHAR(3), never FLOAT, and never a
        // currency column that can hold anything. Money is an owned type, so the two columns
        // live on Transactions exactly as the reference DDL has them.
        builder.OwnsOne(transaction => transaction.Money, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("Amount")
                .HasColumnType("decimal(18,2)")
                .IsRequired();

            money.Property(value => value.CurrencyCode)
                .HasColumnName("CurrencyCode")
                .HasColumnType("char(3)")
                .HasDefaultValue(Money.DefaultCurrencyCode)
                .IsRequired();
        });

        builder.Navigation(transaction => transaction.Money).IsRequired();

        builder.Property(transaction => transaction.CreatedAtUtc)
            .HasColumnType("datetime2(3)")
            .IsRequired();

        builder.Property(transaction => transaction.ModifiedAtUtc)
            .HasColumnType("datetime2(3)");

        // design: doc 02 section 1.8 - optimistic concurrency, surfaced to clients as an ETag.
        builder.Property(transaction => transaction.RowVersion)
            .IsRowVersion();

        // Domain events are an in memory concern. They never reach the database.
        builder.Ignore(transaction => transaction.DomainEvents);

        // The physical order of the table. CreatedAtUtc is stamped by the aggregate on Create
        // and never changes, so rows genuinely append.
        builder.HasIndex(transaction => new { transaction.CreatedAtUtc, transaction.Id })
            .HasDatabaseName("IX_Transactions_CreatedAtUtc")
            .IsClustered();

        // design: doc 02 section 3 - the status foreign key index and the descending date
        // index carry the default read paths.
        builder.HasIndex(transaction => transaction.TransactionStatusId)
            .HasDatabaseName("IX_Transactions_TransactionStatusId");

        builder.HasIndex(transaction => transaction.TransactionDate)
            .HasDatabaseName("IX_Transactions_TransactionDate")
            .IsDescending();

        // The doc 07 covering filtered index on Active transactions is added as raw SQL in the
        // migration: it INCLUDEs columns that belong to the owned Money type, which EF's
        // fluent index API cannot express.
    }

    /// <summary>
    /// A stored value outside the smart enum means the table and the domain have diverged,
    /// which is data corruption rather than a business outcome, so it throws.
    /// </summary>
    private static TransactionType ParseType(string name) =>
        TransactionType.TryFromName(name, out var type)
            ? type
            : throw new InvalidOperationException(
                $"Transactions contains the transaction type '{name}', which the domain does not define.");
}
