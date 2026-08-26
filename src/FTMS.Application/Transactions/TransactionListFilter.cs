namespace FTMS.Application.Transactions;

/// <summary>
/// Everything the list query needs, already normalised and clamped.
/// design: doc 05 section 3 and doc 07 section 4.
/// </summary>
public sealed record TransactionListFilter(
    Guid StatusId,
    string StatusName,
    int Page,
    int PageSize,
    string SortBy,
    string SortDirection)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;

    /// <summary>
    /// Hard server side cap. A client asking for a million rows gets 200, not an outage.
    /// design: doc 05 section 3.
    /// </summary>
    public const int MaxPageSize = 200;

    public const string DefaultSortBy = "transactionDate";
    public const string DefaultSortDirection = "desc";

    /// <summary>The sort fields the API is willing to order by. Anything else is a 400.</summary>
    public static readonly IReadOnlySet<string> SortableFields =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "transactionDate",
            "amount",
            "createdAtUtc",
            "transactionType",
        };

    public bool IsDescending => string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

    /// <summary>Rows to skip. Straightforward OFFSET FETCH paging. design: doc 07 section 3 -
    /// the known weakness is deep pages, which is why paging is isolated in this one place:
    /// if telemetry ever shows users paging deep, keyset paging on (TransactionDate, Id)
    /// replaces this single expression.</summary>
    public int Skip => (Page - 1) * PageSize;
}
