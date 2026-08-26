using FTMS.Application.Abstractions;
using FTMS.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace FTMS.Application.Behaviors;

/// <summary>
/// Answers repeat queries before a handler ever runs, but only for queries that opt in by
/// implementing <see cref="ICachedQuery"/>.
///
/// design: doc 03 section 7 and doc 07 section 4 - statuses cache for 24 hours, the active
/// list caches per query shape for 45 seconds, and get by id caches not at all. Anything that
/// does not implement the marker passes straight through, so adding a new query is opt in
/// rather than opt out, which is the safe default for a system of record.
///
/// Only successful results are cached. Caching a failure would turn a transient problem into
/// a sticky one for the length of the entry's lifetime.
/// </summary>
public static class CachingDecorator
{
    public sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        ICacheService cache,
        ILogger<QueryHandler<TQuery, TResponse>> logger) : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
        {
            if (query is not ICachedQuery cachedQuery)
            {
                return await inner.Handle(query, cancellationToken);
            }

            var key = cachedQuery.CacheKey;
            Result<TResponse>? failure = null;

            var cached = await cache.GetOrCreateAsync<CacheEnvelope<TResponse>>(
                key,
                async token =>
                {
                    var result = await inner.Handle(query, token);

                    if (result.IsFailure)
                    {
                        // Returning null tells the cache service to store nothing. The failure
                        // is carried out through the closure so the caller sees the real error
                        // without the query having to run twice.
                        failure = result;
                        return null;
                    }

                    return new CacheEnvelope<TResponse>(result.Value);
                },
                cachedQuery.Expiration,
                cancellationToken);

            if (cached is not null)
            {
                return Result.Success(cached.Value);
            }

            logger.LogDebug("Query {CacheKey} failed, so nothing was cached.", key);

            return failure ?? await inner.Handle(query, cancellationToken);
        }
    }

    /// <summary>
    /// Wrapper so a legitimately null or default response is still distinguishable from
    /// "nothing was cached".
    /// </summary>
    private sealed record CacheEnvelope<T>(T Value);
}
