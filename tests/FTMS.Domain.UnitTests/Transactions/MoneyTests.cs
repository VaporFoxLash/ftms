using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;

namespace FTMS.Domain.UnitTests.Transactions;

public class MoneyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    [InlineData(1500)]
    [InlineData(1500.55)]
    [InlineData(99999999999999.99)]
    public void Valid_amounts_are_accepted(decimal amount)
    {
        var result = Money.Create(amount);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(amount);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    [InlineData(-1500.55)]
    public void Negative_amounts_are_refused(decimal amount)
    {
        // design: doc 02 section 3 - CK_Transactions_Amount CHECK (Amount >= 0). Direction is
        // carried by the transaction type, not by the sign, which keeps reporting simple.
        var result = Money.Create(amount);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("money.negative_amount");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData(1.001)]
    [InlineData(0.005)]
    [InlineData(1500.5555)]
    public void More_than_two_decimal_places_is_refused(decimal amount)
    {
        // design: doc 02 section 1.1 - the column is DECIMAL(18,2). Accepting a third decimal
        // here would mean silently rounding someone's money on the way to storage.
        var result = Money.Create(amount);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("money.too_many_decimals");
    }

    [Fact]
    public void A_trailing_zero_scale_is_not_mistaken_for_extra_precision()
    {
        Money.Create(15.00m).IsSuccess.ShouldBeTrue();
        Money.Create(15m).IsSuccess.ShouldBeTrue();
        Money.Create(15.0m).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Currency_defaults_to_zar()
    {
        // design: doc 02 section 1.1 - CHAR(3) ISO 4217, defaulting to ZAR.
        Money.Create(100m).Value.CurrencyCode.ShouldBe("ZAR");
        Money.Create(100m, null).Value.CurrencyCode.ShouldBe("ZAR");
        Money.Create(100m, "   ").Value.CurrencyCode.ShouldBe("ZAR");
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData(" eur ", "EUR")]
    [InlineData("GbP", "GBP")]
    public void Currency_codes_are_normalised_to_upper_case(string input, string expected)
    {
        Money.Create(100m, input).Value.CurrencyCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData("ZA")]
    [InlineData("ZARR")]
    [InlineData("Z4R")]
    [InlineData("R$ ")]
    public void Codes_that_are_not_three_letters_are_refused(string code)
    {
        var result = Money.Create(100m, code);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("money.invalid_currency_code");
    }

    [Fact]
    public void Equality_is_by_value()
    {
        var first = Money.Create(1500m, "ZAR").Value;
        var second = Money.Create(1500m, "ZAR").Value;
        var differentAmount = Money.Create(1500.01m, "ZAR").Value;
        var differentCurrency = Money.Create(1500m, "USD").Value;

        first.ShouldBe(second);
        (first == second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
        first.ShouldNotBe(differentAmount);
        first.ShouldNotBe(differentCurrency);
    }

    [Fact]
    public void Money_offers_no_arithmetic_at_all()
    {
        // design: doc 02 section 6 - no arithmetic across currencies. FTMS records what
        // currency a transaction was in; it does not convert, so the safest API is none.
        var operators = typeof(Money)
            .GetMethods()
            .Where(method => method.IsSpecialName && method.Name.StartsWith("op_", StringComparison.Ordinal))
            .Select(method => method.Name)
            .ToList();

        operators.ShouldNotContain("op_Addition");
        operators.ShouldNotContain("op_Subtraction");
        operators.ShouldNotContain("op_Multiply");
        operators.ShouldNotContain("op_Division");
    }
}

public class SmartEnumTests
{
    [Fact]
    public void Transaction_type_has_exactly_the_four_documented_values()
    {
        TransactionType.List.Select(type => type.Name)
            .ShouldBe(["Deposit", "Withdrawal", "Transfer", "Payment"], ignoreOrder: true);
    }

    [Fact]
    public void Transaction_status_has_exactly_the_five_seeded_values()
    {
        TransactionStatus.List.Count.ShouldBe(5);
    }

    [Theory]
    [InlineData("Deposit")]
    [InlineData("deposit")]
    [InlineData("DEPOSIT")]
    public void Type_parsing_is_case_insensitive(string name)
    {
        TransactionType.TryFromName(name, out var type).ShouldBeTrue();
        type.ShouldBe(TransactionType.Deposit);
    }

    [Theory]
    [InlineData("Refund")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_type_names_are_rejected_rather_than_coerced(string? name)
    {
        TransactionType.TryFromName(name, out var type).ShouldBeFalse();
        type.ShouldBeNull();
    }

    [Fact]
    public void Statuses_resolve_from_their_seeded_guids()
    {
        TransactionStatus.TryFromValue(
            FTMS.SharedKernel.Constants.TransactionStatusIds.Cancelled,
            out var status).ShouldBeTrue();

        status.ShouldBe(TransactionStatus.Cancelled);
    }

    [Fact]
    public void An_unseeded_guid_resolves_to_nothing()
    {
        TransactionStatus.TryFromValue(Guid.NewGuid(), out var status).ShouldBeFalse();
        status.ShouldBeNull();
    }
}
