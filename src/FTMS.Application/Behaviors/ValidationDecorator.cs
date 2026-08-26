using FluentValidation;
using FTMS.Application.Abstractions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.Behaviors;

/// <summary>
/// Runs every registered FluentValidation validator for the message before the handler sees
/// it, turning failures into a Result rather than an exception.
/// design: doc 03 section 3 - cross cutting concerns are decorators, so handlers stay pure
/// business logic. doc 05 section 1 - the API renders this as 400 with an errors dictionary.
/// </summary>
public static class ValidationDecorator
{
    public sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var failure = await Validate(command, validators, cancellationToken);

            return failure is null
                ? await inner.Handle(command, cancellationToken)
                : Result.Failure<TResponse>(failure);
        }
    }

    public sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        IEnumerable<IValidator<TQuery>> validators) : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
        {
            var failure = await Validate(query, validators, cancellationToken);

            return failure is null
                ? await inner.Handle(query, cancellationToken)
                : Result.Failure<TResponse>(failure);
        }
    }

    private static async Task<ValidationError?> Validate<TMessage>(
        TMessage message,
        IEnumerable<IValidator<TMessage>> validators,
        CancellationToken cancellationToken)
    {
        var applicable = validators as IReadOnlyList<IValidator<TMessage>> ?? [.. validators];
        if (applicable.Count == 0)
        {
            return null;
        }

        var context = new ValidationContext<TMessage>(message);
        var results = await Task.WhenAll(
            applicable.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .GroupBy(
                failure => Camelise(failure.PropertyName),
                failure => failure.ErrorMessage,
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        return failures.Count == 0 ? null : new ValidationError(failures);
    }

    /// <summary>
    /// The API speaks camelCase (doc 05 section 1), so the errors dictionary keys must match
    /// the field names the client actually sent, not the C# property names.
    /// </summary>
    private static string Camelise(string propertyName) =>
        string.IsNullOrEmpty(propertyName)
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
