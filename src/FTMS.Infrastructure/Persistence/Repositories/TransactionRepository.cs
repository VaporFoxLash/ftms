using FTMS.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace FTMS.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write side. Tracked loads only, because everything here is about to change.
/// design: doc 03 section 5.
/// </summary>
internal sealed class TransactionRepository(FtmsDbContext context) : ITransactionRepository
{
    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Transactions.FirstOrDefaultAsync(transaction => transaction.Id == id, cancellationToken);

    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default) =>
        await context.Transactions.AddAsync(transaction, cancellationToken);
}
