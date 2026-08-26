using FTMS.SharedKernel.Results;

namespace FTMS.Application.Abstractions;

/// <summary>
/// A request that changes state. design: doc 03 section 3 - CQRS with a hand rolled
/// dispatcher. MediatR moved to a commercial licence, and for a system with five endpoints
/// the mediator is not the hard part, so we own roughly a hundred lines instead of a
/// dependency. These signatures deliberately match MediatR's shape closely enough that
/// migrating later would be mechanical.
/// </summary>
/// <typeparam name="TResponse">What the command returns on success.</typeparam>
public interface ICommand<TResponse>;

/// <summary>A request that reads state and changes nothing.</summary>
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Marker for a query whose result may be served from cache.
/// design: doc 03 section 7 and doc 07 section 4 - statuses are effectively immutable and
/// cache for 24 hours; the active list caches per query shape for 45 seconds; get by id is
/// deliberately not cached because correctness beats micro savings on a primary key lookup.
/// A query that does not implement this passes straight through the caching decorator.
/// </summary>
public interface ICachedQuery
{
    /// <summary>Fully qualified cache key for this exact query shape.</summary>
    string CacheKey { get; }

    /// <summary>How long the entry stays fresh.</summary>
    TimeSpan Expiration { get; }
}
