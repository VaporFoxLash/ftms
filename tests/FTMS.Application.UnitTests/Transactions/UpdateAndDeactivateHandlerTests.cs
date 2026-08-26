using FTMS.Application.Abstractions;
using FTMS.Application.Caching;
using FTMS.Application.Transactions.Commands.DeactivateTransaction;
using FTMS.Application.Transactions.Commands.UpdateTransaction;
using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.UnitTests.Transactions;

public class UpdateTransactionHandlerTests
{
    private static readonly DateTime NewDate = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] CurrentVersion = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] StaleVersion = [9, 9, 9, 9, 9, 9, 9, 9];

    private readonly ITransactionRepository _repository = Substitute.For<ITransactionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();

    private UpdateTransactionHandler Handler => new(_repository, _unitOfWork, _cache);

    [Fact]
    public async Task A_fresh_etag_on_an_active_record_updates_and_invalidates()
    {
        var transaction = ApplicationTestData.ActiveTransaction(CurrentVersion);
        _repository.GetByIdAsync(transaction.Id, Arg.Any<CancellationToken>()).Returns(transaction);

        var result = await Handler.Handle(
            new UpdateTransactionCommand(transaction.Id, NewDate, "Transfer", CurrentVersion),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        transaction.TransactionDate.ShouldBe(NewDate);
        transaction.Type.ShouldBe(TransactionType.Transfer);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _cache.Received(1).RemoveByPrefix(CacheKeys.TransactionListPrefix);
    }

    [Fact]
    public async Task A_stale_etag_is_a_conflict_and_nothing_is_saved()
    {
        // design: doc 05 section 6 - a stale ETag gets 412 Precondition Failed so the user
        // refetches and reapplies. Silent last writer wins is not acceptable here.
        var transaction = ApplicationTestData.ActiveTransaction(CurrentVersion);
        _repository.GetByIdAsync(transaction.Id, Arg.Any<CancellationToken>()).Returns(transaction);

        var result = await Handler.Handle(
            new UpdateTransactionCommand(transaction.Id, NewDate, "Transfer", StaleVersion),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("transaction.concurrency_conflict");
        result.Error.Type.ShouldBe(ErrorType.Conflict);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _cache.DidNotReceive().RemoveByPrefix(Arg.Any<string>());
    }

    [Fact]
    public async Task A_missing_transaction_is_a_not_found()
    {
        var id = Guid.CreateVersion7();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Transaction?)null);

        var result = await Handler.Handle(
            new UpdateTransactionCommand(id, NewDate, "Deposit", CurrentVersion),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Editing_a_historical_record_is_refused_by_the_domain_not_the_handler()
    {
        // The handler does not know the rule; it asks the aggregate and reports the answer.
        var transaction = ApplicationTestData.ActiveTransaction(CurrentVersion);
        transaction.Complete().IsSuccess.ShouldBeTrue();
        _repository.GetByIdAsync(transaction.Id, Arg.Any<CancellationToken>()).Returns(transaction);

        var result = await Handler.Handle(
            new UpdateTransactionCommand(transaction.Id, NewDate, "Deposit", CurrentVersion),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("transaction.not_editable");
        _cache.DidNotReceive().RemoveByPrefix(Arg.Any<string>());
    }
}

public class DeactivateTransactionHandlerTests
{
    private readonly ITransactionRepository _repository = Substitute.For<ITransactionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();

    private DeactivateTransactionHandler Handler => new(_repository, _unitOfWork, _cache);

    [Fact]
    public async Task Deactivating_an_active_transaction_archives_it()
    {
        var transaction = ApplicationTestData.ActiveTransaction();
        _repository.GetByIdAsync(transaction.Id, Arg.Any<CancellationToken>()).Returns(transaction);

        var result = await Handler.Handle(
            new DeactivateTransactionCommand(transaction.Id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        transaction.Status.ShouldBe(TransactionStatus.Inactive);
        _cache.Received(1).RemoveByPrefix(CacheKeys.TransactionListPrefix);
    }

    [Fact]
    public async Task Deactivating_twice_succeeds_both_times()
    {
        // design: doc 05 section 7 - DELETE is idempotent, 204 on repeat.
        var transaction = ApplicationTestData.ActiveTransaction();
        _repository.GetByIdAsync(transaction.Id, Arg.Any<CancellationToken>()).Returns(transaction);
        var command = new DeactivateTransactionCommand(transaction.Id);

        (await Handler.Handle(command, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await Handler.Handle(command, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        transaction.Status.ShouldBe(TransactionStatus.Inactive);
    }

    [Fact]
    public async Task An_id_that_never_existed_is_a_not_found()
    {
        var id = Guid.CreateVersion7();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Transaction?)null);

        var result = await Handler.Handle(new DeactivateTransactionCommand(id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        _cache.DidNotReceive().RemoveByPrefix(Arg.Any<string>());
    }
}
