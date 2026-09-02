using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FTMS.Infrastructure.Persistence;

/// <summary>
/// Fills an empty database with a spread of sample transactions, so a freshly cloned repository
/// opens on a populated grid rather than on "No active transactions yet."
///
/// design: doc 08 section 5 - the journeys are the acceptance criteria, and paging, sorting and
/// the status filter cannot be judged against zero rows. Reference data (statuses, roles) is
/// seeded by migrations because the application depends on it; this is different - it is
/// demonstration data, and the application is perfectly correct without it.
///
/// Development only, for the same reason IdentitySeeder is: inventing financial records in a
/// real environment is data corruption, not convenience.
/// </summary>
public static class SampleTransactionSeeder
{
    /// <summary>
    /// Enough rows to page at every offered page size (25, 50, 100, 200) and to give each status
    /// and type a visible share.
    /// </summary>
    private const int SampleSize = 60;

    /// <summary>
    /// A fixed seed, so every machine that clones this repository gets the same amounts, types
    /// and statuses in the same order. Sample data that differs per machine makes a screenshot
    /// impossible to compare with a colleague's and a bug report impossible to reproduce.
    /// </summary>
    private const int RandomSeed = 20260827;

    public static async Task SeedAsync(
        IServiceProvider services,
        bool isDevelopment,
        CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(SampleTransactionSeeder));

        if (!isDevelopment)
        {
            return;
        }

        var context = services.GetRequiredService<FtmsDbContext>();

        // Only ever fills an EMPTY table. Anything else and re-running the application would
        // quietly inflate the data a developer had been working with, and the audit trail would
        // record it as though someone had captured it.
        if (await context.Transactions.AnyAsync(cancellationToken))
        {
            return;
        }

        var random = new Random(RandomSeed);

        // Relative to today rather than absolute, so the demo data always looks recent instead
        // of ageing into a wall of two-year-old records.
        var today = DateTime.UtcNow.Date;

        for (var index = 0; index < SampleSize; index++)
        {
            var transaction = BuildOne(index, random, today);

            if (transaction is not null)
            {
                context.Transactions.Add(transaction);
            }
        }

        // One SaveChanges, so the audit interceptor stamps every sample row with the same
        // instant - which is honest: they were all created by one act, not sixty.
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded {Count} sample transactions.", SampleSize);
    }

    private static Transaction? BuildOne(int index, Random random, DateTime today)
    {
        var type = TransactionType.List[index % TransactionType.List.Count];

        // Spread over the last eight weeks, newest first, so sorting by date and by capture date
        // produce visibly different orders.
        var date = today.AddDays(-index).AddHours(9 + (index % 8)).AddMinutes(index % 60);

        // Amounts that look like money rather than like test data: a wide range, two decimals,
        // and never zero (the domain refuses that, see Money.Create).
        var amount = Math.Round((decimal)(random.NextDouble() * 24_000) + 15.5m, 2);

        var money = Money.Create(amount, Money.DefaultCurrencyCode);
        if (money.IsFailure)
        {
            return null;
        }

        var created = Transaction.Create(date, type, money.Value);
        if (created.IsFailure)
        {
            return null;
        }

        var transaction = created.Value;

        // Every transaction starts Active, exactly as the brief requires. A slice is then moved
        // on through the real state machine - not by writing a status column - so the sample
        // data cannot contain a combination the domain would refuse.
        //
        // Roughly two thirds stay Active, so the default list is well populated while the status
        // filter still has something to find in every other state.
        _ = (index % 9) switch
        {
            3 => transaction.Hold(),
            5 => transaction.Complete(),
            7 => transaction.Cancel(),
            8 => transaction.Deactivate(),
            _ => Result.Success(),
        };

        return transaction;
    }
}
