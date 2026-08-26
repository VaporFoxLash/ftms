using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Constants;
using FTMS.SharedKernel.Results;

namespace FTMS.Domain.UnitTests.Transactions;

public class TransactionCreationTests
{
    private static readonly DateTime AnyDate = new(2026, 8, 26, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_transaction_starts_active()
    {
        // design: doc 02 section 5 - every new transaction starts Active, per the brief.
        var transaction = TransactionBuilder.Active().Build();

        transaction.Status.ShouldBe(TransactionStatus.Active);
        transaction.TransactionStatusId.ShouldBe(TransactionStatusIds.Active);
    }

    [Fact]
    public void A_new_transaction_gets_a_time_ordered_guid_and_a_created_stamp()
    {
        var first = TransactionBuilder.Active().Build();
        var second = TransactionBuilder.Active().Build();

        first.Id.ShouldNotBe(Guid.Empty);

        // design: doc 02 section 1.2 - GUID v7 keys carry their creation time in the leading
        // bytes, so they sort by creation time in byte / string order.
        //
        // Note the comparison used here. Guid.CompareTo orders by the struct's internal fields
        // (_a as an int, then _b and _c as shorts, then the trailing bytes), not left to right,
        // so it does NOT observe v7 monotonicity. Ordinal string order does.
        // SQL Server's uniqueidentifier order is a third ordering again: it compares the LAST
        // six bytes first. See the index note in the Infrastructure entity configuration.
        string.CompareOrdinal(first.Id.ToString(), second.Id.ToString()).ShouldBeLessThan(0);
        first.CreatedAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
        first.ModifiedAtUtc.ShouldBeNull("a record that has never been changed has no modified stamp.");
    }

    [Fact]
    public void A_new_transaction_raises_a_created_event()
    {
        var transaction = TransactionBuilder.Active().Build();

        transaction.DomainEvents.ShouldHaveSingleItem();
    }

    [Fact]
    public void Create_rejects_a_missing_date()
    {
        var money = Money.Create(100m).Value;

        var result = Transaction.Create(default, TransactionType.Deposit, money);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("transaction.date_required");
    }

    [Fact]
    public void Create_stores_the_date_in_utc_whatever_kind_it_arrives_as()
    {
        // design: doc 02 section 1.4 - all timestamps stored in UTC, clients convert for
        // display. SAST is UTC+2, so a local time stored verbatim is two hours wrong.
        var local = new DateTime(2026, 8, 26, 11, 30, 0, DateTimeKind.Local);
        var money = Money.Create(100m).Value;

        var transaction = Transaction.Create(local, TransactionType.Deposit, money).Value;

        transaction.TransactionDate.ShouldBe(local.ToUniversalTime());
        transaction.TransactionDate.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void Create_treats_an_unspecified_kind_as_already_utc()
    {
        var unspecified = new DateTime(2026, 8, 26, 9, 30, 0, DateTimeKind.Unspecified);
        var money = Money.Create(100m).Value;

        var transaction = Transaction.Create(unspecified, TransactionType.Deposit, money).Value;

        transaction.TransactionDate.Kind.ShouldBe(DateTimeKind.Utc);
        transaction.TransactionDate.Hour.ShouldBe(9);
    }

    [Fact]
    public void Create_refuses_a_null_type_or_money_because_those_are_bugs_not_business_outcomes()
    {
        var money = Money.Create(100m).Value;

        Should.Throw<ArgumentNullException>(() => Transaction.Create(AnyDate, null!, money));
        Should.Throw<ArgumentNullException>(() => Transaction.Create(AnyDate, TransactionType.Deposit, null!));
    }
}

public class TransactionUpdateTests
{
    private static readonly DateTime NewDate = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Active")]
    [InlineData("Pending")]
    public void UpdateDetails_is_allowed_in_the_working_states(string status)
    {
        var transaction = TransactionStateMachineTests.TransactionIn(
            TransactionStateMachineTests.StatusNamed(status));

        var result = transaction.UpdateDetails(NewDate, TransactionType.Transfer);

        result.IsSuccess.ShouldBeTrue();
        transaction.TransactionDate.ShouldBe(NewDate);
        transaction.Type.ShouldBe(TransactionType.Transfer);
        transaction.ModifiedAtUtc.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    [InlineData("Inactive")]
    public void UpdateDetails_is_refused_on_historical_records(string status)
    {
        // design: doc 05 section 6 - those records are history, and history does not get
        // edited, it gets superseded. The API turns this into a 409.
        var transaction = TransactionStateMachineTests.TransactionIn(
            TransactionStateMachineTests.StatusNamed(status));
        var originalDate = transaction.TransactionDate;
        var originalType = transaction.Type;

        var result = transaction.UpdateDetails(NewDate, TransactionType.Transfer);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("transaction.not_editable");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        transaction.TransactionDate.ShouldBe(originalDate);
        transaction.Type.ShouldBe(originalType);
    }

    [Fact]
    public void UpdateDetails_rejects_a_missing_date()
    {
        var transaction = TransactionBuilder.Active().Build();

        var result = transaction.UpdateDetails(default, TransactionType.Payment);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("transaction.date_required");
    }

    [Fact]
    public void UpdateDetails_never_touches_amount_currency_or_status()
    {
        // design: doc 05 section 6 - amount, currency and status are not in the request DTO
        // at all, so they cannot even be attempted. The aggregate offers no way in either.
        var transaction = TransactionBuilder.Active().WithAmount(1500m).InCurrency("USD").Build();

        transaction.UpdateDetails(NewDate, TransactionType.Payment).IsSuccess.ShouldBeTrue();

        transaction.Money.Amount.ShouldBe(1500m);
        transaction.Money.CurrencyCode.ShouldBe("USD");
        transaction.Status.ShouldBe(TransactionStatus.Active);
    }
}
