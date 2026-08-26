namespace FTMS.Domain.Transactions;

/// <summary>
/// The write side seam. The Application layer declares what it needs, Infrastructure supplies
/// it, and dependency injection wires them at the composition root.
/// design: doc 03 section 1.
///
/// There is deliberately no Delete method. FTMS never physically deletes a row, and the
/// application's own database login has no DELETE permission on Transactions to make sure of
/// it (doc 06 section 5.1). Archiving is <see cref="Transaction.Deactivate"/>.
/// </summary>
public interface ITransactionRepository
{
    /// <summary>
    /// Loads a transaction for modification, tracked. Returns null when the id has never
    /// existed. Returns transactions in any status, including Inactive, because "fetch by id"
    /// is the audit window (doc 05 section 4).
    /// </summary>
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stages a new transaction. Nothing hits the database until the unit of work saves.</summary>
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default);
}
