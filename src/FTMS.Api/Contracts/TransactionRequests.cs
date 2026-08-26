namespace FTMS.Api.Contracts;

/// <summary>
/// design: doc 05 section 5 - the server assigns the id, sets status to Active per the brief,
/// and stamps CreatedAtUtc, so none of those appear here. currencyCode is optional and
/// defaults to ZAR.
/// </summary>
public sealed record CreateTransactionRequest(
    DateTime TransactionDate,
    string TransactionType,
    decimal Amount,
    string? CurrencyCode);

/// <summary>
/// design: doc 05 section 6 - only transactionDate and transactionType are modifiable, exactly
/// per the brief. Amount, currency and status are not on this DTO AT ALL, so a client cannot
/// even attempt them: the contract refuses before any validator has to. Status changes get
/// their own explicit endpoints when workflow arrives, because "update the date" and "cancel a
/// transaction" are different business acts with different audit meanings.
///
/// The expected RowVersion is not here either: it travels in the If-Match header, where HTTP
/// says a precondition belongs.
/// </summary>
public sealed record UpdateTransactionRequest(
    DateTime TransactionDate,
    string TransactionType);
