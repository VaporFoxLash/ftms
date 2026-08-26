namespace FTMS.Application.Abstractions;

/// <summary>
/// Thrown when the database rejects a write because the row changed underneath us between
/// load and save.
///
/// design: doc 03 section 4 - business failures are Results, exceptions are for the
/// exceptional. A lost optimistic concurrency race genuinely is exceptional: the handler
/// already compared the client's ETag against the loaded RowVersion and found it fresh, so
/// reaching here means another writer won in the microseconds since. Infrastructure raises
/// this instead of leaking DbUpdateConcurrencyException upward, which would drag EF Core
/// types into layers that must not know EF exists. The API middleware maps it to 409.
/// </summary>
public sealed class ConcurrencyConflictException(Guid transactionId, Exception? innerException = null)
    : Exception($"Transaction {transactionId} was modified by another writer.", innerException)
{
    public Guid TransactionId { get; } = transactionId;
}
