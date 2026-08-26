using FTMS.Application.Abstractions;
using FTMS.Domain.Transactions;
using FTMS.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FTMS.Infrastructure.Persistence;

/// <summary>
/// The one place that talks to SQL Server. design: doc 03 section 1.
/// </summary>
public sealed class FtmsDbContext(DbContextOptions<FtmsDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<TransactionStatusLookup> TransactionStatuses => Set<TransactionStatusLookup>();

    public DbSet<TransactionAudit> TransactionAudits => Set<TransactionAudit>();

    /// <summary>
    /// Commits the unit of work. The audit interceptor is registered on the options, so it
    /// runs inside this same SaveChanges: the change and the row that records it are one
    /// database transaction, and neither can be committed without the other.
    /// design: doc 03 section 6.
    /// </summary>
    async Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Translate at the boundary so EF Core types never leak into Application or the
            // API. design: doc 03 - Infrastructure points at Application, never the reverse.
            var id = exception.Entries
                .Select(entry => entry.Entity)
                .OfType<Transaction>()
                .Select(transaction => transaction.Id)
                .FirstOrDefault();

            throw new ConcurrencyConflictException(id, exception);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FtmsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
