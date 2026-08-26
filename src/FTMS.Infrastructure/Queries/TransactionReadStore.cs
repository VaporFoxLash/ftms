using FTMS.Application.Abstractions;
using FTMS.Application.Transactions;
using FTMS.Application.TransactionStatuses;
using FTMS.Domain.Transactions;
using FTMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FTMS.Infrastructure.Queries;

/// <summary>
/// The read side, projecting straight into DTOs with AsNoTracking so EF never materialises an
/// entity it will not track. design: doc 03 section 5 and doc 07 section 4 - this removes most
/// of EF's read overhead, which is exactly why the design starts with EF everywhere and keeps
/// Dapper behind a written trigger rather than paying the two idiom cost up front.
///
/// This class is also the Dapper seam. When the doc 07 trigger fires (two consecutive weeks of
/// missed p95 plus profiling evidence pointing at EF query shape, not the database or the
/// network), a Dapper implementation of ITransactionReadStore replaces this one method at a
/// time, and no handler changes.
/// </summary>
internal sealed class TransactionReadStore(FtmsDbContext context) : ITransactionReadStore
{
    public async Task<PagedResult<TransactionDto>> ListAsync(
        TransactionListFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.TransactionStatusId == filter.StatusId);

        // Counting before paging is what lets the envelope tell clients whether more data
        // exists, so they never have to guess. design: doc 05 section 3.
        var totalCount = await query.CountAsync(cancellationToken);

        // The projection stays a plain object initialiser rather than a call to a mapping
        // helper: EF Core translates expression trees, and a method call it cannot translate
        // is a runtime failure, not a compile error. The smart enum's Name is resolved after
        // materialisation for the same reason.
        var rows = await Sort(query, filter)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Select(transaction => new
            {
                transaction.Id,
                transaction.TransactionDate,
                transaction.Type,
                transaction.Money.Amount,
                transaction.Money.CurrencyCode,
                transaction.CreatedAtUtc,
                transaction.ModifiedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new TransactionDto(
                row.Id,
                row.TransactionDate,
                row.Type.Name,
                row.Amount,
                row.CurrencyCode,
                filter.StatusName,
                row.CreatedAtUtc,
                row.ModifiedAtUtc))
            .ToList();

        return new PagedResult<TransactionDto>(items, filter.Page, filter.PageSize, totalCount);
    }

    public async Task<TransactionDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // design: doc 05 section 4 - no status filter here. A transaction in any status is
        // returned, including Inactive, because fetch by id is the audit window.
        var row = await context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.Id == id)
            .Select(transaction => new
            {
                transaction.Id,
                transaction.TransactionDate,
                transaction.Type,
                transaction.TransactionStatusId,
                transaction.Money.Amount,
                transaction.Money.CurrencyCode,
                transaction.CreatedAtUtc,
                transaction.ModifiedAtUtc,
                transaction.RowVersion,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var statusName = TransactionStatus.TryFromValue(row.TransactionStatusId, out var status)
            ? status.Name
            : throw new InvalidOperationException(
                $"Transaction {row.Id} references status {row.TransactionStatusId}, which is not seeded.");

        var dto = new TransactionDto(
            row.Id,
            row.TransactionDate,
            row.Type.Name,
            row.Amount,
            row.CurrencyCode,
            statusName,
            row.CreatedAtUtc,
            row.ModifiedAtUtc);

        return new TransactionDetail(dto, ETag.From(row.RowVersion));
    }

    public async Task<IReadOnlyList<TransactionStatusDto>> ListStatusesAsync(
        CancellationToken cancellationToken = default) =>
        await context.TransactionStatuses
            .AsNoTracking()
            .OrderBy(status => status.StatusName)
            .Select(status => new TransactionStatusDto(status.Id, status.StatusName))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Ordering is applied from an allow list validated in the Application layer, never from a
    /// raw client string, so no client input reaches the query as an identifier.
    /// </summary>
    private static IQueryable<Transaction> Sort(IQueryable<Transaction> query, TransactionListFilter filter) =>
        (filter.SortBy.ToLowerInvariant(), filter.IsDescending) switch
        {
            ("amount", true) => query.OrderByDescending(transaction => transaction.Money.Amount),
            ("amount", false) => query.OrderBy(transaction => transaction.Money.Amount),
            ("createdatutc", true) => query.OrderByDescending(transaction => transaction.CreatedAtUtc),
            ("createdatutc", false) => query.OrderBy(transaction => transaction.CreatedAtUtc),
            ("transactiontype", true) => query.OrderByDescending(transaction => transaction.Type),
            ("transactiontype", false) => query.OrderBy(transaction => transaction.Type),
            (_, false) => query.OrderBy(transaction => transaction.TransactionDate)
                .ThenBy(transaction => transaction.Id),

            // The default, and the one the doc 07 covering filtered index is built for.
            _ => query.OrderByDescending(transaction => transaction.TransactionDate)
                .ThenBy(transaction => transaction.Id),
        };
}
