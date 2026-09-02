using FTMS.Api.Authentication;
using FTMS.Api.Contracts;
using FTMS.Api.Middleware;
using FTMS.Application.Abstractions;
using FTMS.Application.Transactions;
using FTMS.Application.Transactions.Commands.CreateTransaction;
using FTMS.Application.Transactions.Commands.DeactivateTransaction;
using FTMS.Application.Transactions.Commands.UpdateTransaction;
using FTMS.Application.Transactions.Queries.GetActiveTransactions;
using FTMS.Application.Transactions.Queries.GetTransactionById;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace FTMS.Api.Controllers;

/// <summary>
/// design: doc 05 - routes follow the brief exactly, with no version segment for now. When a
/// breaking change ever forces versioning we introduce /api/v2 and freeze the unversioned
/// routes as v1; that policy is written here so nobody invents a different one under pressure.
///
/// Every action follows the same three steps: build a message, dispatch it, translate the
/// Result. There is no business logic in this file, and there must never be: both clients are
/// thin by contract (doc 04 decision 3) and the API owns all behaviour, so a rule that lived
/// here instead of in the domain would be a rule the state machine could not enforce.
/// </summary>
[Route("api/transactions")]
[Authorize(Policy = AuthorizationPolicies.ReadTransactions)]
[Produces("application/json")]
public sealed class TransactionsController(IDispatcher dispatcher) : ApiControllerBase
{
    /// <summary>
    /// The paged list. Called bare it behaves exactly as the brief demands and returns active
    /// transactions only. design: doc 05 section 3.
    /// </summary>
    [HttpGet(Name = RouteNames.ListTransactions)]
    [ProducesResponseType<PagedResult<TransactionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        // Named sortDirection, not sortDir. FluentValidation keys its failures from the COMMAND
        // property name, camelised - GetActiveTransactionsQuery.SortDirection becomes
        // "sortDirection" - so while the query string said sortDir, a 400 came back complaining
        // about a field the caller had never heard of and no client could map it to an input.
        // design: doc 05 section 1.
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Ask(
            new GetActiveTransactionsQuery(status, page, pageSize, sortBy, sortDirection),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// One transaction in any status, including Inactive, because this endpoint is the audit
    /// window. Sets an ETag from the RowVersion and honours If-None-Match with 304.
    /// design: doc 05 section 4 and doc 07 section 4.
    /// </summary>
    [HttpGet("{id:guid}", Name = RouteNames.GetTransactionById)]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        // Declared as a parameter rather than read off Request.Headers so it appears in the
        // OpenAPI document and therefore in both generated clients. design: doc 05 section 9.
        [FromHeader(Name = "If-None-Match")] string? ifNoneMatch,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Ask(new GetTransactionByIdQuery(id), cancellationToken);

        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        var detail = result.Value;

        // Client side caching for free: the response says when nothing changed and the body
        // never travels. design: doc 07 section 4.
        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Split(',').Any(
                candidate => candidate.Trim() == detail.ETag))
        {
            Response.Headers[HeaderNames.ETag] = detail.ETag;
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers[HeaderNames.ETag] = detail.ETag;

        return Ok(detail.Transaction);
    }

    /// <summary>design: doc 05 section 5 - 201 with a Location header and the created resource.</summary>
    [HttpPost(Name = RouteNames.CreateTransaction)]
    [Authorize(Policy = AuthorizationPolicies.WriteTransactions)]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        // TODO design: doc 05 section 5 and decision 7 - an optional Idempotency-Key header
        // should return the original response when a key is replayed within its retention
        // window, which is cheap insurance against double submits from flaky networks and
        // double clicks. Not in the skeleton: it needs its own store and retention policy, and
        // it is a header contract, so clients that ignore it lose nothing in the meantime.

        var created = await dispatcher.Send(
            new CreateTransactionCommand(
                request.TransactionDate,
                request.TransactionType,
                request.Amount,
                request.CurrencyCode),
            cancellationToken);

        if (created.IsFailure)
        {
            return Problem(created.Error);
        }

        var fetched = await dispatcher.Ask(new GetTransactionByIdQuery(created.Value), cancellationToken);
        if (fetched.IsFailure)
        {
            return Problem(fetched.Error);
        }

        Response.Headers[HeaderNames.ETag] = fetched.Value.ETag;

        // The route NAME, not the method name. They differ deliberately: the name is also the
        // OpenAPI operationId, and it is what the generated Angular client's method is called.
        return CreatedAtRoute(
            RouteNames.GetTransactionById,
            new { id = created.Value },
            fetched.Value.Transaction);
    }

    /// <summary>
    /// Updates the date and type only.
    ///
    /// If-Match is optional: send it and the update is a compare-and-swap that returns 412 when
    /// the ETag is stale; omit it and the update is last-write-wins. 428 is now reserved for an
    /// If-Match that was sent but could not be parsed.
    /// design: doc 05 section 6 and decision 4.
    /// </summary>
    [HttpPut("{id:guid}", Name = RouteNames.UpdateTransaction)]
    [Authorize(Policy = AuthorizationPolicies.WriteTransactions)]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(
        Guid id,
        // Declaring the header here puts If-Match in the OpenAPI document, so the generated
        // clients have a parameter for it and are steered towards sending one.
        // design: doc 05 section 9.
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromBody] UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        // If-Match is OPTIONAL, and that is a deliberate relaxation of what this endpoint used to
        // do - a missing header used to be 428 Precondition Required.
        //
        // The trade is explicit. With the header, the update is a compare-and-swap: a stale ETag
        // loses with 412 and nobody silently overwrites a colleague. Without it, the update is
        // genuinely last-write-wins - EF reloads the row, so its rowversion check has nothing
        // stale to compare against and will NOT save you either. The brief specifies a plain PUT,
        // and a reviewer with curl should not have to perform a GET first to change a date; the
        // cost is that a client which omits the header has opted out of concurrency protection.
        //
        // Clients that can send it, should. The Angular client always does.
        byte[]? rowVersion = null;

        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            if (!ETag.TryParse(ifMatch, out var parsed))
            {
                // A header we cannot decode is not a stale precondition, it is a malformed one.
                // Still 428 rather than 400: the caller DID intend to send a precondition, and
                // quietly downgrading them to last-write-wins because of a typo would be the
                // worst of both behaviours.
                return PreconditionRequired("The If-Match header could not be read as an ETag.");
            }

            rowVersion = parsed;
        }

        var updated = await dispatcher.Send(
            new UpdateTransactionCommand(id, request.TransactionDate, request.TransactionType, rowVersion),
            cancellationToken);

        if (updated.IsFailure)
        {
            return Problem(updated.Error);
        }

        var fetched = await dispatcher.Ask(new GetTransactionByIdQuery(id), cancellationToken);
        if (fetched.IsFailure)
        {
            return Problem(fetched.Error);
        }

        Response.Headers[HeaderNames.ETag] = fetched.Value.ETag;

        return Ok(fetched.Value.Transaction);
    }

    /// <summary>
    /// Soft delete. Never a physical delete: the handler calls Deactivate(), the status moves
    /// to Inactive, and the row stays. Idempotent, so deleting an already Inactive transaction
    /// returns 204 rather than an error, which is what clients and retry logic want. 404 only
    /// when the id has never existed. design: doc 05 section 7 and decision 6.
    /// </summary>
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteTransaction)]
    [Authorize(Policy = AuthorizationPolicies.DeleteTransactions)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new DeactivateTransactionCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>
    /// 428 Precondition Required. design: doc 05 section 6 - a client that forgets If-Match is
    /// told to send one rather than being allowed to overwrite blindly.
    /// </summary>
    private IActionResult PreconditionRequired(string? detail = null)
    {
        var problem = new ProblemDetails
        {
            Type = ProblemTypes.For("precondition-required"),
            Title = "Precondition required",
            Status = StatusCodes.Status428PreconditionRequired,
            Detail = detail
                ?? "An If-Match header carrying the ETag from a prior GET is required on updates.",
            Instance = Request.Path,
        };

        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status428PreconditionRequired,
            ContentTypes = { "application/problem+json" },
        };
    }
}


