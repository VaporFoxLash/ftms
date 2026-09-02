using FTMS.Api.Middleware;
using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace FTMS.Api.Controllers;

/// <summary>
/// Turns a failed <see cref="Result"/> into an RFC 9457 ProblemDetails response, in one place.
///
/// design: doc 05 section 1 and doc 03 decision 2 - the Result errors from the Application
/// layer map onto HTTP here and nowhere else. Controllers contain no business logic: they
/// build a message, hand it to the dispatcher, and translate the answer.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Maps a failure onto a status code.
    ///
    /// The one wrinkle: doc 05 wants a stale ETag to be 412 Precondition Failed, not the 409
    /// that Conflict normally produces. The error TYPE cannot tell those apart, because an
    /// illegal state transition is also a Conflict, so the concurrency case is identified by
    /// its stable error CODE. That is exactly why DomainErrors names its codes.
    /// </summary>
    protected IActionResult Problem(Error error)
    {
        if (error is ValidationError validationError)
        {
            var problem = new ValidationProblemDetails(
                validationError.Failures.ToDictionary(pair => pair.Key, pair => pair.Value))
            {
                Type = ProblemTypes.For(error.Code),
                Title = "One or more validation errors occurred",
                Status = StatusCodes.Status400BadRequest,
                Detail = error.Message,
                Instance = HttpContext.Request.Path,
            };

            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" },
            };
        }

        var statusCode = error.Code == ConcurrencyConflictCode
            ? StatusCodes.Status412PreconditionFailed
            : error.Type switch
            {
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                ErrorType.Locked => StatusCodes.Status423Locked,
                _ => StatusCodes.Status500InternalServerError,
            };

        var details = new ProblemDetails
        {
            Type = ProblemTypes.For(error.Code),
            Title = TitleFor(statusCode),
            Status = statusCode,

            // A 500 never explains itself to the client. design: doc 05 section 1.
            Detail = statusCode == StatusCodes.Status500InternalServerError
                ? "The request could not be completed. The failure has been logged."
                : error.Message,
            Instance = HttpContext.Request.Path,
        };

        details.Extensions["traceId"] = HttpContext.TraceIdentifier;

        return new ObjectResult(details)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>
    /// The code DomainErrors uses for a lost optimistic concurrency check. Aliased from the
    /// domain rather than retyped as a literal, so renaming the code cannot leave this mapping
    /// silently pointing at a string nothing produces any more.
    /// </summary>
    private const string ConcurrencyConflictCode = DomainErrors.Transaction.ConcurrencyConflictCode;

    private static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status412PreconditionFailed => "Precondition failed",
        StatusCodes.Status423Locked => "Account locked",
        _ => "An unexpected error occurred",
    };
}
