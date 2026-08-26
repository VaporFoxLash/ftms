using FluentValidation;
using FTMS.Application.Abstractions;
using FTMS.Domain.Transactions;

namespace FTMS.Application.Transactions.Commands.CreateTransaction;

/// <summary>
/// design: doc 05 section 5 - the server assigns the id, sets status to Active per the brief,
/// and stamps CreatedAtUtc. None of those are inputs.
/// </summary>
public sealed record CreateTransactionCommand(
    DateTime TransactionDate,
    string TransactionType,
    decimal Amount,
    string? CurrencyCode = null) : ICommand<Guid>;

/// <summary>
/// design: doc 05 section 5 - validation runs before the handler even sees the command.
/// The rules mirror the domain's own invariants deliberately: the validator gives the client
/// a field level 400 with a helpful message, the domain refuses regardless of who calls it.
/// Duplication here is defence in depth, not an accident.
/// </summary>
internal sealed class CreateTransactionValidator : AbstractValidator<CreateTransactionCommand>
{
    public CreateTransactionValidator()
    {
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

        RuleFor(command => command.Amount)
            .GreaterThan(0m)
            .WithMessage("Amount must be greater than zero.")
            .Must(amount => decimal.Round(amount, Money.DecimalPlaces) == amount)
            .WithMessage("Amount must have at most two decimal places.");

        RuleFor(command => command.CurrencyCode)
            .Length(Money.CurrencyCodeLength)
            .WithMessage("Currency code must be a three letter ISO 4217 code, for example ZAR.")
            .When(command => !string.IsNullOrWhiteSpace(command.CurrencyCode));
    }
}
