using FluentValidation;
using FTMS.Application.Abstractions;
using FTMS.Application.Caching;
using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.Transactions.Commands.DeactivateTransaction;

/// <summary>
/// Soft delete. design: doc 05 section 7 - never a physical delete. The handler loads the
/// aggregate and calls Deactivate(), which the doc 02 state machine permits from any status.
/// </summary>
public sealed record DeactivateTransactionCommand(Guid Id) : ICommand<Unit>;

internal sealed class DeactivateTransactionValidator : AbstractValidator<DeactivateTransactionCommand>
{
    public DeactivateTransactionValidator() => RuleFor(command => command.Id).NotEmpty();
}

internal sealed class DeactivateTransactionHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    ICacheService cache) : ICommandHandler<DeactivateTransactionCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DeactivateTransactionCommand command, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (transaction is null)
        {
            // design: doc 05 section 7 - 404 only when the id has never existed.
            return Result.Failure<Unit>(DomainErrors.Transaction.NotFound(command.Id));
        }

        // Deactivating an already Inactive transaction succeeds without changing anything,
        // which is what makes DELETE idempotent for clients and retry logic.
        var deactivated = transaction.Deactivate();
        if (deactivated.IsFailure)
        {
            return Result.Failure<Unit>(deactivated.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        cache.RemoveByPrefix(CacheKeys.TransactionListPrefix);

        return Result.Success(Unit.Value);
    }
}
