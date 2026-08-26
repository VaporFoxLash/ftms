using System.Text.Json;
using FTMS.Application.Abstractions;
using FTMS.Domain.Transactions;
using FTMS.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FTMS.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes a TransactionAudits row for every change to a transaction, by diffing tracked
/// entities at save time.
///
/// design: doc 03 section 6, deciding doc 02's open question. Domain events were the
/// alternative and they are more explicit, but the compliance trail must be unconditional, so
/// it belongs to the persistence pipeline rather than to whoever remembered to raise an event.
/// This cannot be forgotten by a future developer, it captures changes added later for free,
/// and it lives entirely in Infrastructure.
///
/// doc 08 section 3 verifies this with an integration test that asserts every write endpoint
/// leaves exactly the expected rows, proving the interceptor cannot be bypassed by any code path.
/// </summary>
public sealed class AuditSaveChangesInterceptor(ICurrentUser currentUser) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        WriteIndented = false,
    };

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            WriteAuditRows(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            WriteAuditRows(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void WriteAuditRows(DbContext context)
    {
        // One timestamp for the whole save, so every row written by one business act shares
        // an instant and an auditor can group them without guessing.
        var changedAtUtc = DateTime.UtcNow;
        var changedBy = currentUser.UserName;

        var entries = context.ChangeTracker
            .Entries<Transaction>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Deleted)
            {
                // design: doc 01 decision 4 and doc 06 section 5.1 - FTMS never physically
                // deletes a transaction, and the application's SQL login has no DELETE
                // permission on this table. Reaching here means someone wrote code the
                // architecture forbids, so fail loudly rather than audit the crime politely.
                throw new InvalidOperationException(
                    $"A physical delete of transaction {entry.Entity.Id} was attempted. "
                    + "FTMS soft deletes only: archive with Deactivate() instead.");
            }

            var isCreate = entry.State == EntityState.Added;

            context.Add(new TransactionAudit(
                entry.Entity.Id,
                ClassifyChange(entry, isCreate),
                isCreate ? null : Serialise(BeforeSnapshot(entry)),
                Serialise(AfterSnapshot(entry.Entity)),
                changedBy,
                changedAtUtc));
        }
    }

    /// <summary>
    /// "Someone corrected the date" and "someone archived this record" are different events to
    /// an auditor, so the status move gets its own change type. design: doc 05 section 6.
    /// </summary>
    private static string ClassifyChange(EntityEntry<Transaction> entry, bool isCreate)
    {
        if (isCreate)
        {
            return AuditChangeTypes.Created;
        }

        return entry.Property(transaction => transaction.TransactionStatusId).IsModified
            ? AuditChangeTypes.StatusChanged
            : AuditChangeTypes.Updated;
    }

    private static Dictionary<string, object?> AfterSnapshot(Transaction transaction) => new()
    {
        ["id"] = transaction.Id,
        ["transactionDate"] = transaction.TransactionDate,
        ["transactionType"] = transaction.Type.Name,
        ["transactionStatusId"] = transaction.TransactionStatusId,
        ["status"] = transaction.Status.Name,
        ["amount"] = transaction.Money.Amount,
        ["currencyCode"] = transaction.Money.CurrencyCode,
        ["createdAtUtc"] = transaction.CreatedAtUtc,
        ["modifiedAtUtc"] = transaction.ModifiedAtUtc,
    };

    private static Dictionary<string, object?> BeforeSnapshot(EntityEntry<Transaction> entry)
    {
        var original = entry.OriginalValues;

        // Money is an owned type, so EF tracks it as a SEPARATE entry. Reading
        // entry.OriginalValues alone would report the amount as whatever it is now, which on a
        // financial audit table is not a rounding error, it is a lie. Reach into the owned
        // entry for the real before values.
        var moneyOriginal = entry
            .References
            .FirstOrDefault(reference => reference.Metadata.Name == nameof(Transaction.Money))?
            .TargetEntry?
            .OriginalValues;

        var statusId = original.GetValue<Guid>(nameof(Transaction.TransactionStatusId));

        return new Dictionary<string, object?>
        {
            ["id"] = original.GetValue<Guid>(nameof(Transaction.Id)),
            ["transactionDate"] = original.GetValue<DateTime>(nameof(Transaction.TransactionDate)),
            ["transactionType"] = original.GetValue<TransactionType>(nameof(Transaction.Type))?.Name,
            ["transactionStatusId"] = statusId,
            ["status"] = TransactionStatus.TryFromValue(statusId, out var status) ? status.Name : null,
            ["amount"] = moneyOriginal?.GetValue<decimal>(nameof(Money.Amount)),
            ["currencyCode"] = moneyOriginal?.GetValue<string>(nameof(Money.CurrencyCode)),
            ["createdAtUtc"] = original.GetValue<DateTime>(nameof(Transaction.CreatedAtUtc)),
            ["modifiedAtUtc"] = original.GetValue<DateTime?>(nameof(Transaction.ModifiedAtUtc)),
        };
    }

    private static string Serialise(Dictionary<string, object?> snapshot) =>
        JsonSerializer.Serialize(snapshot, SnapshotOptions);
}
