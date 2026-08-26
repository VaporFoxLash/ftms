using FTMS.Domain.Transactions;

namespace FTMS.Domain.UnitTests.Transactions;

/// <summary>
/// One builder per aggregate, so a test states only what it actually cares about.
/// design: doc 08 section 4 - no shared fixtures that accumulate mystery state.
/// </summary>
internal sealed class TransactionBuilder
{
    private DateTime _date = new(2026, 8, 26, 9, 30, 0, DateTimeKind.Utc);
    private TransactionType _type = TransactionType.Deposit;
    private decimal _amount = 1500m;
    private string _currencyCode = Money.DefaultCurrencyCode;

    public static TransactionBuilder Active() => new();

    public TransactionBuilder On(DateTime dateUtc)
    {
        _date = dateUtc;
        return this;
    }

    public TransactionBuilder OfType(TransactionType type)
    {
        _type = type;
        return this;
    }

    public TransactionBuilder WithAmount(decimal amount)
    {
        _amount = amount;
        return this;
    }

    public TransactionBuilder InCurrency(string currencyCode)
    {
        _currencyCode = currencyCode;
        return this;
    }

    public Transaction Build()
    {
        var money = Money.Create(_amount, _currencyCode);
        money.IsSuccess.ShouldBeTrue("the builder's own money must be valid; use Money.Create directly to test invalid money.");

        var transaction = Transaction.Create(_date, _type, money.Value);
        transaction.IsSuccess.ShouldBeTrue("the builder's own arrangement must be valid.");

        return transaction.Value;
    }
}
