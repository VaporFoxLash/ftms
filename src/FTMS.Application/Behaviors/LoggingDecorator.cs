using System.Diagnostics;
using FTMS.Application.Abstractions;
using FTMS.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace FTMS.Application.Behaviors;

/// <summary>
/// One structured log line per dispatched message, with its duration and outcome.
/// design: doc 06 section 7 - authorisation denials, concurrency conflicts and validation
/// misses are security signals, so they get logged as named events with thresholds to alert
/// on. Note what is NOT logged: no message payloads, because a command body carries amounts
/// and dates that are personal information under POPIA. Only the type name, the outcome and
/// the error code travel to the log store.
/// </summary>
public static class LoggingDecorator
{
    public sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        ILogger<CommandHandler<TCommand, TResponse>> logger) : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken) =>
            Observe(logger, "command", typeof(TCommand).Name, () => inner.Handle(command, cancellationToken));
    }

    public sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        ILogger<QueryHandler<TQuery, TResponse>> logger) : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken) =>
            Observe(logger, "query", typeof(TQuery).Name, () => inner.Handle(query, cancellationToken));
    }

    private static async Task<Result<TResponse>> Observe<TResponse>(
        ILogger logger,
        string kind,
        string name,
        Func<Task<Result<TResponse>>> handle)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await handle();
            stopwatch.Stop();

            if (result.IsSuccess)
            {
                logger.LogInformation(
                    "Handled {MessageKind} {MessageName} in {ElapsedMilliseconds} ms.",
                    kind,
                    name,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogWarning(
                    "{MessageKind} {MessageName} failed with {ErrorCode} ({ErrorType}) after {ElapsedMilliseconds} ms.",
                    kind,
                    name,
                    result.Error.Code,
                    result.Error.Type,
                    stopwatch.ElapsedMilliseconds);
            }

            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogError(
                exception,
                "{MessageKind} {MessageName} threw after {ElapsedMilliseconds} ms.",
                kind,
                name,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}
