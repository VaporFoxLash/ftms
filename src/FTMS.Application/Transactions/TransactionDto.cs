namespace FTMS.Application.Transactions;

/// <summary>
/// The wire shape of a transaction, identical for list items and get by id.
/// design: doc 05 sections 3 and 4. camelCase and UTC ISO 8601 are applied by the API's
/// JSON options, not by this type.
/// </summary>
public sealed record TransactionDto(
    Guid Id,
    DateTime TransactionDate,
    string TransactionType,
    decimal Amount,
    string CurrencyCode,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

/// <summary>
/// A single transaction plus its concurrency token.
///
/// The ETag is kept out of <see cref="TransactionDto"/> on purpose: doc 05 puts it in the
/// ETag response header, not in the body, and a client that reads it from the body would be
/// coupled to a representation the contract never promised. The controller lifts it into the
/// header and returns the plain DTO.
/// </summary>
public sealed record TransactionDetail(TransactionDto Transaction, string ETag);

/// <summary>
/// The paging envelope, so clients never guess whether more data exists.
/// design: doc 05 section 3 - paging is mandatory from day one because unpaged financial
/// lists become an outage two years later.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
