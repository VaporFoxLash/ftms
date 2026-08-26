using FTMS.Application.Abstractions;
using FTMS.Application.Caching;
using FTMS.Application.Transactions.Commands.CreateTransaction;
using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.UnitTests.Transactions;

public class CreateTransactionHandlerTests
{
    private static readonly DateTime AnyDate = new(2026, 8, 26, 9, 30, 0, DateTimeKind.Utc);

    private readonly ITransactionRepository _repository = Substitute.For<ITransactionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();

    private CreateTransactionHandler Handler => new(_repository, _unitOfWork, _cache);

    [Fact]
    public async Task A_valid_command_adds_an_active_transaction_and_saves_once()
    {
        var command = new CreateTransactionCommand(AnyDate, "Deposit", 1500m, "ZAR");

        var result = await Handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);

        await _repository.Received(1).AddAsync(
            Arg.Is<Transaction>(transaction =>
                transaction.Status == TransactionStatus.Active
                && transaction.Type == TransactionType.Deposit
                && transaction.Money.Amount == 1500m
                && transaction.Money.CurrencyCode == "ZAR"),
            Arg.Any<CancellationToken>());

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Currency_defaults_to_zar_when_the_client_omits_it()
    {
        // design: doc 05 section 5 - currencyCode is optional and defaults to ZAR.
        var command = new CreateTransactionCommand(AnyDate, "Payment", 42m);

        await Handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<Transaction>(transaction => transaction.Money.CurrencyCode == "ZAR"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_successful_create_invalidates_the_list_cache()
    {
        // design: doc 07 section 4 - all three commands invalidate by prefix tx:list: on success.
        await Handler.Handle(new CreateTransactionCommand(AnyDate, "Deposit", 100m), CancellationToken.None);

        _cache.Received(1).RemoveByPrefix(CacheKeys.TransactionListPrefix);
    }

    [Fact]
    public async Task An_unknown_type_fails_without_touching_the_repository_or_the_cache()
    {
        var command = new CreateTransactionCommand(AnyDate, "Refund", 100m);

        var result = await Handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("transaction.unknown_type");

        await _repository.DidNotReceive().AddAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _cache.DidNotReceive().RemoveByPrefix(Arg.Any<string>());
    }

    [Fact]
    public async Task Invalid_money_fails_without_saving_or_invalidating()
    {
        var command = new CreateTransactionCommand(AnyDate, "Deposit", -5m);

        var result = await Handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("money.negative_amount");
        _cache.DidNotReceive().RemoveByPrefix(Arg.Any<string>());
    }
}
