using System.Collections.Concurrent;
using FTMS.Application.Abstractions;
using FTMS.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace FTMS.Application.Dispatching;

/// <summary>
/// The hand rolled dispatcher. design: doc 03 section 3, decision 1.
///
/// The one awkward bit of any dispatcher: <see cref="Send"/> receives the command through
/// <see cref="ICommand{TResponse}"/>, but the container has to be asked for the closed
/// generic <c>ICommandHandler&lt;CreateTransactionCommand, Guid&gt;</c>, which needs the
/// concrete type. Rather than reflect on every call, we build a small non generic wrapper
/// once per command type and cache it, so warm dispatches are a dictionary lookup and a
/// virtual call.
/// </summary>
internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> Wrappers = new();

    public Task<Result<TResponse>> Send<TResponse>(
        ICommand<TResponse> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = (CommandWrapper<TResponse>)Wrappers.GetOrAdd(
            (command.GetType(), typeof(TResponse)),
            static key => Activate(typeof(CommandWrapper<,>), key));

        return wrapper.Handle(command, services, cancellationToken);
    }

    public Task<Result<TResponse>> Ask<TResponse>(
        IQuery<TResponse> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wrapper = (QueryWrapper<TResponse>)Wrappers.GetOrAdd(
            (query.GetType(), typeof(TResponse)),
            static key => Activate(typeof(QueryWrapper<,>), key));

        return wrapper.Handle(query, services, cancellationToken);
    }

    private static object Activate(Type openWrapper, (Type Request, Type Response) key) =>
        Activator.CreateInstance(openWrapper.MakeGenericType(key.Request, key.Response))
        ?? throw new InvalidOperationException($"Could not create a dispatcher wrapper for {key.Request}.");

    private abstract class CommandWrapper<TResponse>
    {
        public abstract Task<Result<TResponse>> Handle(
            object command,
            IServiceProvider services,
            CancellationToken cancellationToken);
    }

    private sealed class CommandWrapper<TCommand, TResponse> : CommandWrapper<TResponse>
        where TCommand : ICommand<TResponse>
    {
        public override Task<Result<TResponse>> Handle(
            object command,
            IServiceProvider services,
            CancellationToken cancellationToken) =>
            services
                .GetRequiredService<ICommandHandler<TCommand, TResponse>>()
                .Handle((TCommand)command, cancellationToken);
    }

    private abstract class QueryWrapper<TResponse>
    {
        public abstract Task<Result<TResponse>> Handle(
            object query,
            IServiceProvider services,
            CancellationToken cancellationToken);
    }

    private sealed class QueryWrapper<TQuery, TResponse> : QueryWrapper<TResponse>
        where TQuery : IQuery<TResponse>
    {
        public override Task<Result<TResponse>> Handle(
            object query,
            IServiceProvider services,
            CancellationToken cancellationToken) =>
            services
                .GetRequiredService<IQueryHandler<TQuery, TResponse>>()
                .Handle((TQuery)query, cancellationToken);
    }
}
