using FTMS.Application.Abstractions;
using FTMS.Application.Transactions;
using FTMS.Application.Transactions.Queries.GetActiveTransactions;
using FTMS.Application.Transactions.Queries.GetTransactionById;
using FTMS.Application.TransactionStatuses;
using FTMS.Application.TransactionStatuses.Queries.GetTransactionStatuses;
using FTMS.SharedKernel.Constants;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.UnitTests.Transactions;

public class GetActiveTransactionsQueryTests
{
    [Fact]
    public void A_bare_query_defaults_to_active_page_one_fifty_rows_newest_first()
    {
        // design: doc 05 section 3 - called bare it behaves exactly as the brief demands.
        var filter = new GetActiveTransactionsQuery().ToFilter();

        filter.StatusId.ShouldBe(TransactionStatusIds.Active);
        filter.Page.ShouldBe(1);
        filter.PageSize.ShouldBe(50);
        filter.SortBy.ShouldBe("transactionDate");
        filter.IsDescending.ShouldBeTrue();
        filter.Skip.ShouldBe(0);
    }

    [Fact]
    public void Page_size_is_clamped_to_the_server_cap()
    {
        // design: doc 05 section 3 - pageSize is capped at 200 server side.
        new GetActiveTransactionsQuery(PageSize: 5000).ToFilter().PageSize.ShouldBe(200);
        new GetActiveTransactionsQuery(PageSize: 200).ToFilter().PageSize.ShouldBe(200);
        new GetActiveTransactionsQuery(PageSize: 10).ToFilter().PageSize.ShouldBe(10);
    }

    [Fact]
    public void Skip_follows_the_page_and_page_size()
    {
        new GetActiveTransactionsQuery(Page: 3, PageSize: 25).ToFilter().Skip.ShouldBe(50);
    }

    [Fact]
    public void The_cache_key_matches_the_documented_shape()
    {
        // design: doc 07 section 4 - tx:list:{status}:{page}:{pageSize}:{sortBy}:{sortDir}
        var query = new GetActiveTransactionsQuery("Pending", 2, 25, "amount", "asc");

        query.CacheKey.ShouldBe("tx:list:Pending:2:25:amount:asc");
        query.Expiration.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Two_queries_of_the_same_shape_share_a_cache_key_and_different_shapes_do_not()
    {
        new GetActiveTransactionsQuery().CacheKey
            .ShouldBe(new GetActiveTransactionsQuery("Active", 1, 50, "transactionDate", "desc").CacheKey);

        new GetActiveTransactionsQuery(Page: 1).CacheKey
            .ShouldNotBe(new GetActiveTransactionsQuery(Page: 2).CacheKey);
    }

    [Theory]
    [InlineData("Actve")]
    [InlineData("deleted")]
    [InlineData("' OR 1=1--")]
    public void An_unknown_status_fails_validation_rather_than_returning_an_empty_list(string status)
    {
        // design: doc 05 section 3 - an unknown status value returns 400, not an empty list,
        // so typos fail loudly.
        var result = new GetActiveTransactionsValidator().Validate(new GetActiveTransactionsQuery(status));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("inactive")]
    [InlineData("CANCELLED")]
    [InlineData(null)]
    public void Known_statuses_pass_validation_case_insensitively(string? status)
    {
        new GetActiveTransactionsValidator().Validate(new GetActiveTransactionsQuery(status)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void An_unsortable_field_fails_validation()
    {
        // Only an allow list is sortable, which also means the read store never interpolates
        // a client supplied column name into SQL.
        new GetActiveTransactionsValidator()
            .Validate(new GetActiveTransactionsQuery(SortBy: "rowVersion"))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task The_handler_returns_whatever_the_read_store_paged()
    {
        var readStore = Substitute.For<ITransactionReadStore>();
        var page = new PagedResult<TransactionDto>([], 1, 50, 0);
        readStore.ListAsync(Arg.Any<TransactionListFilter>(), Arg.Any<CancellationToken>()).Returns(page);

        var result = await new GetActiveTransactionsHandler(readStore)
            .Handle(new GetActiveTransactionsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalPages.ShouldBe(0);
    }
}

public class GetTransactionByIdHandlerTests
{
    private readonly ITransactionReadStore _readStore = Substitute.For<ITransactionReadStore>();

    [Fact]
    public async Task A_known_id_returns_the_transaction_and_its_etag()
    {
        var id = Guid.CreateVersion7();
        var detail = new TransactionDetail(
            new TransactionDto(id, ApplicationTestData.AnyDate, "Deposit", 1500m, "ZAR", "Active",
                ApplicationTestData.AnyDate, null),
            "\"AAAAAAAAB9E=\"");
        _readStore.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(detail);

        var result = await new GetTransactionByIdHandler(_readStore)
            .Handle(new GetTransactionByIdQuery(id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ETag.ShouldBe("\"AAAAAAAAB9E=\"");
    }

    [Fact]
    public async Task An_unknown_id_is_a_not_found()
    {
        var id = Guid.CreateVersion7();
        _readStore.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((TransactionDetail?)null);

        var result = await new GetTransactionByIdHandler(_readStore)
            .Handle(new GetTransactionByIdQuery(id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void Get_by_id_deliberately_opts_out_of_caching()
    {
        // design: doc 07 section 4 - get by id skips the server cache and relies on the ETag
        // with 304 instead, because correctness beats micro savings on a key lookup.
        new GetTransactionByIdQuery(Guid.CreateVersion7()).ShouldNotBeAssignableTo<ICachedQuery>();
    }
}

public class GetTransactionStatusesQueryTests
{
    [Fact]
    public void Statuses_cache_for_a_day_under_one_key()
    {
        var query = new GetTransactionStatusesQuery();

        query.CacheKey.ShouldBe("tx:statuses");
        query.Expiration.ShouldBe(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task The_handler_returns_the_seeded_statuses()
    {
        var readStore = Substitute.For<ITransactionReadStore>();
        IReadOnlyList<TransactionStatusDto> statuses =
            [new TransactionStatusDto(TransactionStatusIds.Active, "Active")];
        readStore.ListStatusesAsync(Arg.Any<CancellationToken>()).Returns(statuses);

        var result = await new GetTransactionStatusesHandler(readStore)
            .Handle(new GetTransactionStatusesQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem();
    }
}
