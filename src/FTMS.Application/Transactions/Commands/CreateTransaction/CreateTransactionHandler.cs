using FTMS.Application.Abstractions;
using FTMS.Application.Caching;
using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.Transactions.Commands.CreateTransaction;

internal sealed class CreateTransactionHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    ICacheService cache) : ICommandHandler<CreateTransactionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateTransactionCommand command, CancellationToken cancellationToken)
    {
        if (!TransactionType.TryFromName(command.TransactionType, out var type))
        {
            return Result.Failure<Guid>(DomainErrors.Transaction.UnknownType);
        }

        var money = Money.Create(command.Amount, command.CurrencyCode);
        if (money.IsFailure)
        {
            return Result.Failure<Guid>(money.Error);
        }

        var transaction = Transaction.Create(command.TransactionDate, type, money.Value);
        if (transaction.IsFailure)
        {
            return Result.Failure<Guid>(transaction.Error);
        }

        await repository.AddAsync(transaction.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // design: doc 07 section 4 - all three commands invalidate the tx:list: family on
        // success. Only on success: a failed write must not evict a still correct cache.
        cache.RemoveByPrefix(CacheKeys.TransactionListPrefix);

        return Result.Success(transaction.Value.Id);
    }
}
