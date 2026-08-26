using FTMS.Api.Authentication;
using FTMS.Application.Abstractions;
using FTMS.Application.TransactionStatuses;
using FTMS.Application.TransactionStatuses.Queries.GetTransactionStatuses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FTMS.Api.Controllers;

/// <summary>design: doc 05 section 2.</summary>
[Route("api/transactionstatuses")]
[Authorize(Policy = AuthorizationPolicies.ReadTransactions)]
[Produces("application/json")]
public sealed class TransactionStatusesController(IDispatcher dispatcher) : ApiControllerBase
{
    /// <summary>
    /// All five statuses. No paging, the set is tiny and effectively immutable, which also
    /// makes this the perfect cache warm up call for clients. Served from the 24 hour cache,
    /// so after the first hit it costs no database round trip.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TransactionStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await dispatcher.Ask(new GetTransactionStatusesQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
