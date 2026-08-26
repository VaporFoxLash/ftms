using FTMS.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace FTMS.Api.Middleware;

/// <summary>
/// The one place unhandled exceptions become responses.
///
/// design: doc 05 section 1 - every failure response has the same ProblemDetails (RFC 9457)
/// shape, so both clients write one error handler. Anything unexpected becomes a 500 with no
/// internal details leaked: the stack trace goes to the log, never to the client, because an
/// error message is an information disclosure surface (doc 06 section 4).
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ConcurrencyConflictException exception)
        {
            // The row changed between the handler's ETag comparison and the save. Rare, but
            // real, and the honest answer is the same one a stale ETag gets: refetch and
            // reapply. design: doc 05 section 6.
            logger.LogWarning(
                exception,
                "Concurrency conflict saving transaction {TransactionId}.",
                exception.TransactionId);

            await WriteProblem(
                context,
                StatusCodes.Status412PreconditionFailed,
                "transaction-concurrency-conflict",
                "Precondition failed",
                "The transaction was changed by someone else. Refetch it and reapply your change.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client hung up. design: doc 07 section 4 - cancellation tokens flow from the
            // HTTP request all the way into SQL so abandoned requests stop costing us. This is
            // not an error worth a log entry or a response nobody will read.
            logger.LogDebug("Request {Path} was cancelled by the client.", context.Request.Path);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception handling {Path}.", context.Request.Path);

            await WriteProblem(
                context,
                StatusCodes.Status500InternalServerError,
                "internal-server-error",
                "An unexpected error occurred",
                "The request could not be completed. The failure has been logged.");
        }
    }

    private static async Task WriteProblem(
        HttpContext context,
        int statusCode,
        string errorCode,
        string title,
        string detail)
    {
        if (context.Response.HasStarted)
        {
            // Too late to change the response. Logging already happened; say nothing further
            // rather than corrupting a half written body.
            return;
        }

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.For(errorCode),
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path,
        };

        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
    }
}

/// <summary>
/// Builds the ProblemDetails <c>type</c> URI. design: doc 05 section 1 - the error code is part
/// of the API contract, so these URIs are stable and renaming one is a breaking change.
/// </summary>
public static class ProblemTypes
{
    public const string BaseUri = "https://ftms.example/errors/";

    public static string For(string errorCode) =>
        BaseUri + errorCode.Replace('.', '-').Replace('_', '-');
}
