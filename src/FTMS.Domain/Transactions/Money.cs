using FTMS.SharedKernel.Primitives;
using FTMS.SharedKernel.Results;

namespace FTMS.Domain.Transactions;

/// <summary>
/// An amount in a currency. Equality is by value.
/// design: doc 02 section 1.1 - DECIMAL(18,2), never FLOAT, because floating point money is
/// how you lose cents at scale. Multi currency arithmetic is out of scope: FTMS records what
/// currency a transaction was in, it does not convert between them, so this type deliberately
/// offers no operators.
/// </summary>
public sealed class Money : ValueObject
{
    /// <summary>ISO 4217 codes are three letters. design: doc 02, CHAR(3).</summary>
    public const int CurrencyCodeLength = 3;

    /// <summary>South African Rand, the default for a system built for a South African business.</summary>
    public const string DefaultCurrencyCode = "ZAR";

    /// <summary>The scale of the DECIMAL(18,2) column money is stored in.</summary>
    public const int DecimalPlaces = 2;

    private Money(decimal amount, string currencyCode)
    {
        Amount = amount;
        CurrencyCode = currencyCode;
    }

    public decimal Amount { get; }

    public string CurrencyCode { get; }

    /// <summary>
    /// Builds a valid Money or explains why it could not. An invalid amount is an expected
    /// outcome of user input, so it is a Result, not an exception. design: doc 03 section 4.
    /// </summary>
    /// <param name="amount">Zero or greater, at most two decimal places.</param>
    /// <param name="currencyCode">Three letters; defaults to ZAR when null or blank.</param>
    public static Result<Money> Create(decimal amount, string? currencyCode = null)
    {
        if (amount < 0m)
        {
            return Result.Failure<Money>(DomainErrors.Money.NegativeAmount);
        }

        // decimal keeps its own scale, so 15.00m and 15m differ in scale but not in value.
        // Rounding to two places and comparing catches 15.001 without rejecting 15m.
        if (decimal.Round(amount, DecimalPlaces) != amount)
        {
            return Result.Failure<Money>(DomainErrors.Money.TooManyDecimals);
        }

        var code = string.IsNullOrWhiteSpace(currencyCode)
            ? DefaultCurrencyCode
            : currencyCode.Trim().ToUpperInvariant();

        if (code.Length != CurrencyCodeLength || !code.All(char.IsAsciiLetterUpper))
        {
            return Result.Failure<Money>(DomainErrors.Money.InvalidCurrencyCode);
        }

        return Result.Success(new Money(decimal.Round(amount, DecimalPlaces), code));
    }

    /// <summary>
    /// Rehydrates a Money that is already known good, for the EF Core materialiser and for
    /// tests. Bypasses validation deliberately: rows already in the database were validated
    /// on the way in, and the CHECK constraint guards the rest.
    /// </summary>
    public static Money FromPersistence(decimal amount, string currencyCode) => new(amount, currencyCode);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return CurrencyCode;
    }

    public override string ToString() => $"{CurrencyCode} {Amount:0.00}";
}
