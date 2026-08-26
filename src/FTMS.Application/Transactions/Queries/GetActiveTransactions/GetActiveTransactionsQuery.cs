using FluentValidation;
using FTMS.Application.Abstractions;
using FTMS.Application.Caching;
using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.Transactions.Queries.GetActiveTransactions;

/// <summary>
/// The paged transaction list.
/// design: doc 05 section 3 and decision 1 - called bare it behaves exactly as the brief
/// demands and returns active transactions only, but auditors and admins can ask for other
/// slices without a new endpoint. Paging is mandatory with a hard server side cap.
/// </summary>
public sealed record GetActiveTransactionsQuery(
    string? Status = null,
    int? Page = null,
    int? PageSize = null,
    string? SortBy = null,
    string? SortDirection = null) : IQuery<PagedResult<TransactionDto>>, ICachedQuery
{
    /// <summary>
    /// Turns loose query string input into a normalised, clamped filter. Safe to call only
    /// after validation has confirmed the status and sort field are known, which the
    /// validation decorator guarantees because it wraps the caching decorator.
    /// </summary>
    public TransactionListFilter ToFilter()
    {
        var status = ResolveStatus() ?? TransactionStatus.Active;

        var page = Math.Max(Page ?? TransactionListFilter.DefaultPage, 1);

        // Clamped rather than rejected: doc 05 says pageSize is capped at 200 server side,
        // so a client asking for more gets 200 rows, not an error.
        var pageSize = Math.Clamp(
            PageSize ?? TransactionListFilter.DefaultPageSize,
            1,
            TransactionListFilter.MaxPageSize);

        var sortBy = string.IsNullOrWhiteSpace(SortBy) ? TransactionListFilter.DefaultSortBy : SortBy;
        var sortDirection = string.IsNullOrWhiteSpace(SortDirection)
            ? TransactionListFilter.DefaultSortDirection
            : SortDirection.ToLowerInvariant();

        return new TransactionListFilter(status.Value, status.Name, page, pageSize, sortBy, sortDirection);
    }

    /// <summary>tx:list:{status}:{page}:{pageSize}:{sortBy}:{sortDir}. design: doc 07 section 4.</summary>
    public string CacheKey => CacheKeys.TransactionList(ToFilter());

    public TimeSpan Expiration => CacheKeys.TransactionListLifetime;

    internal TransactionStatus? ResolveStatus() =>
        string.IsNullOrWhiteSpace(Status)
            ? TransactionStatus.Active
            : TransactionStatus.TryFromName(Status, out var status) ? status : null;
}

internal sealed class GetActiveTransactionsValidator : AbstractValidator<GetActiveTransactionsQuery>
{
    public GetActiveTransactionsValidator()
    {
        // design: doc 05 section 3 - an unknown status value returns 400, not an empty list,
        // so typos fail loudly instead of quietly looking like "no data".
        RuleFor(query => query.Status)
            .Must(status => status is null || TransactionStatus.TryFromName(status, out _))
            .WithMessage("Status must be one of Active, Inactive, Pending, Completed or Cancelled.");

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .When(query => query.Page.HasValue)
            .WithMessage("Page must be 1 or greater.");

        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .When(query => query.PageSize.HasValue)
            .WithMessage("Page size must be 1 or greater.");

        RuleFor(query => query.SortBy)
            .Must(field => field is null || TransactionListFilter.SortableFields.Contains(field))
            .WithMessage(
                "Sort field must be one of "
                + $"{string.Join(", ", TransactionListFilter.SortableFields)}.");

        RuleFor(query => query.SortDirection)
            .Must(direction => direction is null
                || direction.Equals("asc", StringComparison.OrdinalIgnoreCase)
                || direction.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Sort direction must be asc or desc.");
    }
}

internal sealed class GetActiveTransactionsHandler(ITransactionReadStore readStore)
    : IQueryHandler<GetActiveTransactionsQuery, PagedResult<TransactionDto>>
{
    public async Task<Result<PagedResult<TransactionDto>>> Handle(
        GetActiveTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        var page = await readStore.ListAsync(query.ToFilter(), cancellationToken);

        return Result.Success(page);
    }
}
