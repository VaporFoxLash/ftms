using System.Reflection;
using FTMS.Domain.Transactions;

namespace FTMS.Application.UnitTests;

/// <summary>Arrangement helpers for handler tests. design: doc 08 section 4.</summary>
internal static class ApplicationTestData
{
    internal static readonly DateTime AnyDate = new(2026, 8, 26, 9, 30, 0, DateTimeKind.Utc);

    internal static Transaction ActiveTransaction(byte[]? rowVersion = null)
    {
        var money = Money.Create(1500m, "ZAR").Value;
        var transaction = Transaction.Create(AnyDate, TransactionType.Deposit, money).Value;

        if (rowVersion is not null)
        {
            SetRowVersion(transaction, rowVersion);
        }

        return transaction;
    }

    /// <summary>
    /// RowVersion is database generated: SQL Server stamps it, EF Core reads it back, and the
    /// aggregate exposes only a private setter because no business code should ever write it.
    /// A handler test that exercises the ETag comparison needs a known value, so the test
    /// arrangement reaches in through reflection. This is the one place that is acceptable,
    /// and it is confined to test arrangement rather than production code.
    /// </summary>
    private static void SetRowVersion(Transaction transaction, byte[] rowVersion) =>
        typeof(Transaction)
            .GetProperty(nameof(Transaction.RowVersion), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(transaction, rowVersion);
}
