using FTMS.Application.Abstractions;
using FTMS.Application.Caching;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.TransactionStatuses.Queries.GetTransactionStatuses;

/// <summary>
/// design: doc 05 section 2 - all five statuses, no paging, effectively immutable, served
/// from the long lived cache so after the first hit this endpoint costs no database round trip.
/// </summary>
public sealed record GetTransactionStatusesQuery
    : IQuery<IReadOnlyList<TransactionStatusDto>>, ICachedQuery
{
    public string CacheKey => CacheKeys.TransactionStatuses;

    /// <summary>24 hours. Only a deployment changes the seeded rows. design: doc 07 section 4.</summary>
    public TimeSpan Expiration => CacheKeys.StatusesLifetime;
}

internal sealed class GetTransactionStatusesHandler(ITransactionReadStore readStore)
    : IQueryHandler<GetTransactionStatusesQuery, IReadOnlyList<TransactionStatusDto>>
{
    public async Task<Result<IReadOnlyList<TransactionStatusDto>>> Handle(
        GetTransactionStatusesQuery query,
        CancellationToken cancellationToken)
    {
        var statuses = await readStore.ListStatusesAsync(cancellationToken);

        return Result.Success(statuses);
    }
}
