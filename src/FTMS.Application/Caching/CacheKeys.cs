using FTMS.Application.Transactions;

namespace FTMS.Application.Caching;

/// <summary>
/// Cache keys and lifetimes, fixed in one place.
/// design: doc 07 section 4 and decision 4 - statuses 24 hours, lists 45 seconds with prefix
/// invalidation on writes, get by id via ETag and 304 instead of server caching.
/// </summary>
public static class CacheKeys
{
    /// <summary>Everything list shaped hangs off this prefix so one call invalidates the family.</summary>
    public const string TransactionListPrefix = "tx:list:";

    public const string TransactionStatuses = "tx:statuses";

    /// <summary>Effectively immutable; only a deployment changes the seeded rows.</summary>
    public static readonly TimeSpan StatusesLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Short enough that a stale list is a nuisance rather than a lie. The Angular and WPF
    /// clients hold the same 45 second staleness contract, so every layer agrees on how
    /// fresh is fresh.
    /// </summary>
    public static readonly TimeSpan TransactionListLifetime = TimeSpan.FromSeconds(45);

    /// <summary>tx:list:{status}:{page}:{pageSize}:{sortBy}:{sortDir}</summary>
    public static string TransactionList(TransactionListFilter filter) =>
        $"{TransactionListPrefix}{filter.StatusName}:{filter.Page}:{filter.PageSize}:"
        + $"{filter.SortBy}:{filter.SortDirection}";
}
