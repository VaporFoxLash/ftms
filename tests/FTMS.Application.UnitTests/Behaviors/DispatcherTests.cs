using FTMS.Application.Abstractions;
using FTMS.Application.Behaviors;
using FTMS.Application.Transactions;
using FTMS.Application.Transactions.Commands.CreateTransaction;
using FTMS.Application.Transactions.Commands.DeactivateTransaction;
using FTMS.Application.Transactions.Commands.UpdateTransaction;
using FTMS.Application.Transactions.Queries.GetActiveTransactions;
using FTMS.Application.Transactions.Queries.GetTransactionById;
using FTMS.Application.TransactionStatuses;
using FTMS.Application.TransactionStatuses.Queries.GetTransactionStatuses;
using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FTMS.Application.UnitTests.Behaviors;

/// <summary>
/// The dispatcher and the DI wiring together. design: doc 03 section 3 - the pipeline stays
/// explicit and debuggable, so these tests assert the wiring itself rather than mocking it away.
/// </summary>
public class DispatcherTests
{
    private static ServiceProvider BuildContainer(Action<IServiceCollection>? customise = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();

        // Infrastructure's job in production; substituted here so the Application layer can be
        // exercised end to end without a database.
        services.AddSingleton(Substitute.For<ITransactionRepository>());
        services.AddSingleton(Substitute.For<IUnitOfWork>());
        services.AddSingleton(Substitute.For<ITransactionReadStore>());
        services.AddSingleton(Substitute.For<ICacheService>());

        customise?.Invoke(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Every_handler_the_application_declares_can_actually_be_resolved()
    {
        // Catches the classic Scrutor mistake of a handler that silently never registers,
        // which otherwise only shows up as a 500 in production.
        using var provider = BuildContainer();

        provider.GetRequiredService<ICommandHandler<CreateTransactionCommand, Guid>>().ShouldNotBeNull();
        provider.GetRequiredService<ICommandHandler<UpdateTransactionCommand, Unit>>().ShouldNotBeNull();
        provider.GetRequiredService<ICommandHandler<DeactivateTransactionCommand, Unit>>().ShouldNotBeNull();
        provider.GetRequiredService<IQueryHandler<GetActiveTransactionsQuery, PagedResult<TransactionDto>>>()
            .ShouldNotBeNull();
        provider.GetRequiredService<IQueryHandler<GetTransactionByIdQuery, TransactionDetail>>()
            .ShouldNotBeNull();
        provider
            .GetRequiredService<IQueryHandler<
                GetTransactionStatusesQuery, IReadOnlyList<TransactionStatusDto>>>()
            .ShouldNotBeNull();
    }

    [Fact]
    public void The_outermost_decorator_is_logging()
    {
        // design: doc 04 section 5 - Logging wraps Validation wraps Caching wraps Handler.
        // Scrutor makes the LAST Decorate call outermost, so registration order is load bearing.
        using var provider = BuildContainer();

        var handler = provider.GetRequiredService<ICommandHandler<CreateTransactionCommand, Guid>>();

        handler.ShouldBeOfType<LoggingDecorator.CommandHandler<CreateTransactionCommand, Guid>>();
    }

    [Fact]
    public async Task The_dispatcher_routes_a_command_through_the_pipeline_to_its_handler()
    {
        using var provider = BuildContainer();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(
            new CreateTransactionCommand(ApplicationTestData.AnyDate, "Deposit", 1500m),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        await provider.GetRequiredService<ITransactionRepository>()
            .Received(1).AddAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validation_rejects_a_bad_command_before_the_handler_runs()
    {
        using var provider = BuildContainer();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(
            new CreateTransactionCommand(ApplicationTestData.AnyDate, "Refund", -1m),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ValidationError>();

        await provider.GetRequiredService<ITransactionRepository>()
            .DidNotReceive().AddAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validation_runs_outside_caching_so_a_bad_query_never_produces_a_cache_key()
    {
        using var provider = BuildContainer();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var cache = provider.GetRequiredService<ICacheService>();

        var result = await dispatcher.Ask(
            new GetActiveTransactionsQuery("Actve"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await cache.DidNotReceiveWithAnyArgs().GetOrCreateAsync<object>(default!, default!, default);
    }

    [Fact]
    public async Task An_unregistered_message_fails_loudly_rather_than_silently()
    {
        using var provider = BuildContainer();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => dispatcher.Send(new UnhandledCommand(), CancellationToken.None));
    }

    private sealed record UnhandledCommand : ICommand<string>;
}
