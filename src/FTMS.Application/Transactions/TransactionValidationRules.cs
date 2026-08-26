namespace FTMS.Application.Transactions;

/// <summary>
/// Validation constants shared by the create and update validators, so the two cannot
/// disagree about what counts as a valid date. design: doc 05 section 5.
/// </summary>
internal static class TransactionValidationRules
{
    /// <summary>
    /// A small tolerance for clock skew between client and server. A transaction dated a
    /// minute into the future is a clock; a transaction dated next year is a typo.
    /// </summary>
    internal static readonly TimeSpan FutureDateTolerance = TimeSpan.FromMinutes(5);

    internal static bool IsNotAbsurdlyInTheFuture(DateTime date) =>
        date.ToUniversalTime() <= DateTime.UtcNow.Add(FutureDateTolerance);
}
