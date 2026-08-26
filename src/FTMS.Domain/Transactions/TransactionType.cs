using FTMS.SharedKernel.Primitives;

namespace FTMS.Domain.Transactions;

/// <summary>
/// The kind of money movement a transaction records.
/// design: doc 02 section 1.5 - the column stays NVARCHAR(50) exactly as the brief
/// specifies, but the domain only ever accepts one of these four, so the column shape
/// matches the spec while the data stays clean. Persisted as <see cref="SmartEnum{TEnum,TKey}.Name"/>.
/// </summary>
public sealed class TransactionType : SmartEnum<TransactionType, string>
{
    public static readonly TransactionType Deposit = new(nameof(Deposit));
    public static readonly TransactionType Withdrawal = new(nameof(Withdrawal));
    public static readonly TransactionType Transfer = new(nameof(Transfer));
    public static readonly TransactionType Payment = new(nameof(Payment));

    private TransactionType(string name)
        : base(name, name)
    {
    }

    /// <summary>Maximum persisted length, per the brief's NVARCHAR(50).</summary>
    public const int MaxLength = 50;
}
