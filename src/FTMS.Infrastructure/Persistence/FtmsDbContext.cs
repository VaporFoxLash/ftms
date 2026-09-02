using FTMS.Application.Abstractions;
using FTMS.Domain.Transactions;
using FTMS.Infrastructure.Identity;
using FTMS.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FTMS.Infrastructure.Persistence;

/// <summary>
/// The one place that talks to SQL Server. design: doc 03 section 1.
///
/// Derives from <see cref="IdentityDbContext{TUser,TRole,TKey}"/> so the identity tables live in
/// the same database and the same migration history as everything else. One context rather than
/// two: a second context would mean a second __EFMigrationsHistory, a second MigrateAsync at
/// startup and a second Respawn configuration, all to separate tables that share a connection,
/// a backup and a restore anyway. design: doc 06 section 3.
/// </summary>
public sealed class FtmsDbContext(DbContextOptions<FtmsDbContext> options)
    : IdentityDbContext<FtmsUser, FtmsRole, Guid>(options), IUnitOfWork
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<TransactionStatusLookup> TransactionStatuses => Set<TransactionStatusLookup>();

    public DbSet<TransactionAudit> TransactionAudits => Set<TransactionAudit>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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
        // Order matters, and it is the reverse of what it was before Identity arrived. The base
        // call is what maps AspNetUsers, AspNetRoles and the five join tables; our own
        // configurations then refine FtmsUser and FtmsRole on top of that mapping. Applying ours
        // first would mean Identity's configuration ran last and overwrote them.
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FtmsDbContext).Assembly);
    }
}
