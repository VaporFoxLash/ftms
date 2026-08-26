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
/// RowVersion is the client's ETag, decoded. design: doc 05 section 6 - silent last writer
/// wins is not acceptable on financial records.
/// </summary>
public sealed record UpdateTransactionCommand(
    Guid Id,
    DateTime TransactionDate,
    string TransactionType,
    byte[] RowVersion) : ICommand<Unit>;

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

        RuleFor(command => command.RowVersion)
            .NotEmpty()
            .WithMessage("An If-Match header carrying the current ETag is required.");
    }
}
