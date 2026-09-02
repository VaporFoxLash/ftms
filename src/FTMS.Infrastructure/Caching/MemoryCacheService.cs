using System.Collections.Concurrent;
using FTMS.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace FTMS.Infrastructure.Caching;

/// <summary>
/// In process cache over IMemoryCache.
///
/// design: doc 03 section 7 - a single API instance next to SQL Server Express does not
/// justify Redis, and doc 07 section 2 notes the cache is not a luxury here: Express caps the
/// buffer pool at roughly 1.4 GB, so caching in the API is load shedding for the database.
/// Swapping to Redis is a new class and a DI line.
///
/// IMemoryCache has no way to enumerate keys, so prefix invalidation needs a key register.
/// That register is the only reason this class is more than a thin pass through.
/// </summary>
internal sealed class MemoryCacheService(IMemoryCache cache) : ICacheService
{
    /// <summary>
    /// Every key currently believed to be in the cache. A ConcurrentDictionary used as a set,
    /// because IMemoryCache can evict under memory pressure without telling us, so this is a
    /// superset. A stale entry here costs one wasted removal, never a stale read.
    ///
    /// Instance, not static. As a static it was shared by every instance in the PROCESS while the
    /// IMemoryCache it describes was not - so two hosts in one process (which is exactly what the
    /// integration tests are) would each invalidate against the other's key register, and one
    /// suite could leave the other believing it had cached keys it never wrote.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> knownKeys = new(StringComparer.Ordinal);

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var created = await factory(cancellationToken);

        if (created is null)
        {
            // Nothing to cache. A failed query must not become a sticky failure.
            return null;
        }

        cache.Set(key, created, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration,
        });

        knownKeys.TryAdd(key, 0);

        return created;
    }

    public void Remove(string key)
    {
        cache.Remove(key);
        knownKeys.TryRemove(key, out _);
    }

    public void RemoveByPrefix(string prefix)
    {
        foreach (var key in knownKeys.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            Remove(key);
        }
    }
}
