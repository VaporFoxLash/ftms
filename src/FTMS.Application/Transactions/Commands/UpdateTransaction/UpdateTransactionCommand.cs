using FluentValidation;
using FTMS.Application.Abstractions;
using FTMS.Domain.Transactions;

namespace FTMS.Application.Transactions.Commands.UpdateTransaction;

/// <summary>
/// design: doc 05 section 6 - only transactionDate and transactionType are modifiable,
/// exactly per the brief. Amount, currency and status are not on this command at all, so
/// they cannot even be attempted. Status changes get their own explicit endpoints when
/// workflow arrives, because "update the date" and "cancel a transaction" are different
/// business acts with different audit meanings.
///
/// RowVersion is the client's ETag, decoded, and is OPTIONAL. When present the update is a
/// compare-and-swap and a stale value is refused; when null the caller has opted out and the
/// update is last-write-wins. design: doc 05 section 6 and the brief, which specifies a plain
/// PUT with no precondition.
/// </summary>
public sealed record UpdateTransactionCommand(
    Guid Id,
    DateTime TransactionDate,
    string TransactionType,
    byte[]? RowVersion = null) : ICommand<Unit>;

internal sealed class UpdateTransactionValidator : AbstractValidator<UpdateTransactionCommand>
{
    public UpdateTransactionValidator()
    {
        RuleFor(command => command.Id)
            .NotEmpty();

        RuleFor(command => command.TransactionDate)
            .NotEmpty()
            .WithMessage("Transaction date is required.")
            .Must(TransactionValidationRules.IsNotAbsurdlyInTheFuture)
            .WithMessage("Transaction date cannot be in the future.");

        RuleFor(command => command.TransactionType)
            .NotEmpty()
            .WithMessage("Transaction type is required.")
            .Must(type => TransactionType.TryFromName(type, out _))
            .WithMessage("Transaction type must be one of Deposit, Withdrawal, Transfer or Payment.");

        // No rule on RowVersion. It used to be NotEmpty, making If-Match mandatory; it is now
        // optional by design. A malformed If-Match never reaches here - the controller rejects
        // that with 428 before dispatching - so the only two states this validator can see are
        // "a decoded rowversion" and "the caller chose not to send one", and both are valid.
    }
}
