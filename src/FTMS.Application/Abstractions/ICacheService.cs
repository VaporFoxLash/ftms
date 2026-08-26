namespace FTMS.Application.Abstractions;

/// <summary>
/// The caching seam. Declared here, implemented over IMemoryCache in Infrastructure.
/// design: doc 03 section 7 - a single API instance next to SQL Server Express does not
/// justify Redis. Because the abstraction exists, Redis later is a new class and a DI line,
/// and the C4 container diagram gains a box, which is exactly the kind of change the
/// architecture is supposed to make visible.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Returns the cached value, or runs <paramref name="factory"/> and caches what it produces.
    /// A factory that returns null caches nothing and yields null, which is how a failed query
    /// avoids becoming a sticky failure for the length of the entry's lifetime.
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Drops one entry.</summary>
    void Remove(string key);

    /// <summary>
    /// Drops every entry whose key starts with <paramref name="prefix"/>. The active list is
    /// cached per query shape (status, page, page size, sort), so invalidating it after a
    /// write means clearing the whole tx:list: family rather than guessing which shapes exist.
    /// design: doc 07 section 4.
    /// </summary>
    void RemoveByPrefix(string prefix);
}
