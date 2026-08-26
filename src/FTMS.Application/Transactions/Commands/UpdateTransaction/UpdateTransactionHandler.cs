using FTMS.Application.Abstractions;
using FTMS.Application.Caching;
using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.Transactions.Commands.UpdateTransaction;

internal sealed class UpdateTransactionHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    ICacheService cache) : ICommandHandler<UpdateTransactionCommand, Unit>
{
    public async Task<Result<Unit>> Handle(UpdateTransactionCommand command, CancellationToken cancellationToken)
    {
        if (!TransactionType.TryFromName(command.TransactionType, out var type))
        {
            return Result.Failure<Unit>(DomainErrors.Transaction.UnknownType);
        }

        var transaction = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (transaction is null)
        {
            return Result.Failure<Unit>(DomainErrors.Transaction.NotFound(command.Id));
        }

        // design: doc 05 section 6 - the client sends the ETag it received on GET, the server
        // compares it to the current RowVersion, and a stale ETag becomes 412 Precondition
        // Failed so the user refetches and reapplies. Checking here catches the ordinary case
        // cheaply; the rowversion concurrency token in the database catches the race between
        // this comparison and the save, surfacing as ConcurrencyConflictException.
        if (!transaction.RowVersion.SequenceEqual(command.RowVersion))
        {
            return Result.Failure<Unit>(DomainErrors.Transaction.ConcurrencyConflict);
        }

        // The domain, not this handler, decides whether a Completed or Cancelled record may
        // be edited. design: doc 02 section 6.
        var updated = transaction.UpdateDetails(command.TransactionDate, type);
        if (updated.IsFailure)
        {
            return Result.Failure<Unit>(updated.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        cache.RemoveByPrefix(CacheKeys.TransactionListPrefix);

        return Result.Success(Unit.Value);
    }
}
