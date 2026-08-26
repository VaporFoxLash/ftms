using FluentValidation;
using FTMS.Application.Abstractions;
using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.Transactions.Queries.GetTransactionById;

/// <summary>
/// design: doc 05 section 4 and decision 2 - returns a transaction in any status, including
/// Inactive, because that endpoint is the audit window. Hiding soft deleted rows here would
/// defeat the whole reason we soft delete.
///
/// Deliberately NOT an ICachedQuery: doc 03 section 7 and doc 07 section 4 keep get by id off
/// the server cache and rely on the ETag with 304 instead, because correctness beats micro
/// savings on a primary key lookup.
/// </summary>
public sealed record GetTransactionByIdQuery(Guid Id) : IQuery<TransactionDetail>;

internal sealed class GetTransactionByIdValidator : AbstractValidator<GetTransactionByIdQuery>
{
    public GetTransactionByIdValidator() => RuleFor(query => query.Id).NotEmpty();
}

internal sealed class GetTransactionByIdHandler(ITransactionReadStore readStore)
    : IQueryHandler<GetTransactionByIdQuery, TransactionDetail>
{
    public async Task<Result<TransactionDetail>> Handle(
        GetTransactionByIdQuery query,
        CancellationToken cancellationToken)
    {
        var detail = await readStore.GetByIdAsync(query.Id, cancellationToken);

        return detail is null
            ? Result.Failure<TransactionDetail>(DomainErrors.Transaction.NotFound(query.Id))
            : Result.Success(detail);
    }
}
