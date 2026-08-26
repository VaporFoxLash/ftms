namespace FTMS.Application.Abstractions;

/// <summary>
/// Commits everything staged in the current request as one transaction.
/// design: doc 03 section 1 - Application declares the need, Infrastructure implements it
/// over the EF Core DbContext, and the audit interceptor rides along on the same SaveChanges
/// so the compliance trail cannot be committed separately from the change it describes.
/// </summary>
public interface IUnitOfWork
{
    /// <returns>The number of state entries written, including audit rows.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
