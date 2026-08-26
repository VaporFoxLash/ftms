using FTMS.Application.Transactions;
using FTMS.Application.TransactionStatuses;

namespace FTMS.Application.Abstractions;

/// <summary>
/// The read side seam. Implemented in Infrastructure with AsNoTracking projections straight
/// into DTOs, so EF never materialises an entity it will not track.
/// design: doc 03 section 5 - one database, separated read and write models in code, not in
/// storage. This is also where the Dapper seam lives: when the doc 07 trigger fires (two
/// consecutive weeks of missed p95 plus profiling evidence pointing at EF query shape), a
/// Dapper implementation of this interface slots in behind it, one method at a time, without
/// touching a handler. Until that trigger fires, nobody adds Dapper.
/// </summary>
public interface ITransactionReadStore
{
    /// <summary>The paged active list, served by the doc 07 covering filtered index.</summary>
    Task<PagedResult<TransactionDto>> ListAsync(
        TransactionListFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One transaction in any status, including Inactive, with its concurrency token.
    /// design: doc 05 section 4 - fetch by id is how support and auditors look at history,
    /// so hiding soft deleted rows here would defeat the reason we soft delete.
    /// </summary>
    Task<TransactionDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>All five seeded statuses.</summary>
    Task<IReadOnlyList<TransactionStatusDto>> ListStatusesAsync(CancellationToken cancellationToken = default);
}
