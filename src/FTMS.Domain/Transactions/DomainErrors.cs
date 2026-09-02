using FTMS.SharedKernel.Results;

namespace FTMS.Domain.Transactions;

/// <summary>
/// Every business failure the transaction domain can produce, named once.
/// design: doc 03 section 4 and doc 05 section 1 - these codes become the
/// ProblemDetails <c>type</c> URI suffix, so they are part of the API contract and
/// renaming one is a breaking change for clients.
/// </summary>
public static class DomainErrors
{
    public static class Transaction
    {
        public static Error NotFound(Guid id) => Error.NotFound(
            "transaction.not_found",
            $"No transaction exists with id {id}.");

        public static Error IllegalTransition(TransactionStatus from, TransactionStatus to) => Error.Conflict(
            "transaction.illegal_transition",
            $"A transaction cannot move from {from.Name} to {to.Name}.");

        public static Error NotEditable(TransactionStatus current) => Error.Conflict(
            "transaction.not_editable",
            $"A {current.Name} transaction is history and cannot be edited. "
            + "Only Active and Pending transactions accept changes.");

        public static readonly Error UnknownType = Error.Validation(
            "transaction.unknown_type",
            "Transaction type must be one of Deposit, Withdrawal, Transfer or Payment.");

        public static readonly Error DateRequired = Error.Validation(
            "transaction.date_required",
            "Transaction date is required.");

        /// <summary>
        /// Exposed as a const, unlike its siblings, because the API layer has to special case
        /// this one code onto 412 rather than the 409 its Conflict type would otherwise produce -
        /// and a const can be referenced by another const, so the mapping cannot drift from the
        /// error the way a retyped string literal would.
        /// </summary>
        public const string ConcurrencyConflictCode = "transaction.concurrency_conflict";

        public static readonly Error ConcurrencyConflict = Error.Conflict(
            ConcurrencyConflictCode,
            "The transaction was changed by someone else. Refetch it and reapply your change.");
    }

    public static class Money
    {
        /// <summary>
        /// Named for what it rejects rather than for the sign, because it rejects zero too.
        /// The code keeps its original spelling: error codes are part of the API contract
        /// (they become the ProblemDetails type URI), so renaming one would break clients that
        /// switch on it.
        /// </summary>
        public static readonly Error NotPositiveAmount = Error.Validation(
            "money.negative_amount",
            "Amount must be greater than zero. Direction is carried by the transaction type, "
            + "not by the sign of the amount.");

        public static readonly Error TooManyDecimals = Error.Validation(
            "money.too_many_decimals",
            "Amount must have at most two decimal places.");

        public static readonly Error InvalidCurrencyCode = Error.Validation(
            "money.invalid_currency_code",
            "Currency code must be a three letter ISO 4217 code, for example ZAR.");

        public static readonly Error CurrencyMismatch = Error.Conflict(
            "money.currency_mismatch",
            "Amounts in different currencies cannot be compared or combined.");
    }
}
