using FTMS.Domain.Transactions.Events;
using FTMS.SharedKernel.Primitives;
using FTMS.SharedKernel.Results;

namespace FTMS.Domain.Transactions;

/// <summary>
/// The aggregate root. Every change goes through a method on this class, there are no public
/// setters, and an invalid state is unrepresentable.
/// design: doc 02 sections 5 and 6. The guarded state machine lives here rather than in a
/// handler or a controller because it is the one rule that must hold no matter which entry
/// point reaches the data.
/// </summary>
public sealed class Transaction : Entity
{
    /// <summary>
    /// The single place the domain reads the clock. Kept private so a TimeProvider seam is one
    /// edit if determinism in tests ever demands it.
    /// </summary>
    private static DateTime UtcNow => DateTime.UtcNow;

    /// <summary>Constructor for the EF Core materialiser only.</summary>
    private Transaction()
    {
        Type = TransactionType.Deposit;
        Money = Money.FromPersistence(0m, Money.DefaultCurrencyCode);
        RowVersion = [];
    }

    private Transaction(Guid id, DateTime transactionDateUtc, TransactionType type, Money money)
        : base(id)
    {
        TransactionDate = transactionDateUtc;
        Type = type;
        Money = money;
        TransactionStatusId = TransactionStatus.Active.Value;
        CreatedAtUtc = UtcNow;
        ModifiedAtUtc = null;
        RowVersion = [];
    }

    /// <summary>When the money moved, in UTC. Clients convert for display. design: doc 02 section 1.4.</summary>
    public DateTime TransactionDate { get; private set; }

    public TransactionType Type { get; private set; }

    /// <summary>
    /// Foreign key onto the seeded TransactionStatuses lookup. Named for the column in the
    /// doc 02 DDL; use <see cref="Status"/> to reason about it in code.
    /// </summary>
    public Guid TransactionStatusId { get; private set; }

    /// <summary>The current status as a smart enum, so code never compares raw GUIDs.</summary>
    public TransactionStatus Status =>
        TransactionStatus.TryFromValue(TransactionStatusId, out var status)
            ? status
            : throw new InvalidOperationException(
                $"Transaction {Id} holds status id {TransactionStatusId}, which is not a seeded status. "
                + "This means the lookup table and the domain have diverged.");

    public Money Money { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    /// <summary>
    /// SQL Server rowversion, surfaced to clients as an ETag.
    /// design: doc 02 section 1.8 and doc 05 section 6 - two users editing the same
    /// transaction cannot silently overwrite each other.
    /// </summary>
    public byte[] RowVersion { get; private set; }

    /// <summary>
    /// Creates a transaction in the Active status, per the brief.
    /// design: doc 02 section 5 - every new transaction starts Active.
    /// </summary>
    public static Result<Transaction> Create(DateTime transactionDateUtc, TransactionType type, Money money)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(money);

        var normalisedDate = NormaliseToUtc(transactionDateUtc);
        if (normalisedDate == default)
        {
            return Result.Failure<Transaction>(DomainErrors.Transaction.DateRequired);
        }

        // design: doc 04 - GUID version 7 keys are time ordered, so inserts append near the end
        // of the clustered index instead of fragmenting it the way random GUIDs do.
        var transaction = new Transaction(Guid.CreateVersion7(), normalisedDate, type, money);
        transaction.Raise(new TransactionCreated(transaction.Id, transaction.CreatedAtUtc));

        return Result.Success(transaction);
    }

    /// <summary>
    /// Changes the date and type. Legal only while the transaction is Active or Pending.
    /// design: doc 05 section 6 - Completed, Cancelled and Inactive records are history, and
    /// history does not get edited, it gets superseded.
    /// </summary>
    public Result UpdateDetails(DateTime transactionDateUtc, TransactionType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!Status.IsWorkingState)
        {
            return Result.Failure(DomainErrors.Transaction.NotEditable(Status));
        }

        var normalisedDate = NormaliseToUtc(transactionDateUtc);
        if (normalisedDate == default)
        {
            return Result.Failure(DomainErrors.Transaction.DateRequired);
        }

        TransactionDate = normalisedDate;
        Type = type;
        Touch();
        Raise(new TransactionDetailsUpdated(Id, UtcNow));

        return Result.Success();
    }

    /// <summary>Active to Pending. Holds the transaction for processing.</summary>
    public Result Hold() => TransitionTo(TransactionStatus.Pending, id => new TransactionHeld(id, UtcNow));

    /// <summary>Pending back to Active. Releases the hold.</summary>
    public Result Release() => TransitionTo(TransactionStatus.Active, id => new TransactionReleased(id, UtcNow));

    /// <summary>Settles the transaction. Terminal business outcome.</summary>
    public Result Complete() =>
        TransitionTo(TransactionStatus.Completed, id => new TransactionCompleted(id, UtcNow));

    /// <summary>Cancels the transaction. Terminal business outcome.</summary>
    public Result Cancel() =>
        TransitionTo(TransactionStatus.Cancelled, id => new TransactionCancelled(id, UtcNow));

    /// <summary>
    /// Archives the transaction. This is what DELETE means in FTMS; nothing is ever physically
    /// removed. Calling it on an already Inactive transaction succeeds without doing anything,
    /// which is what makes the DELETE endpoint idempotent.
    /// design: doc 02 section 5 and doc 05 section 7.
    /// </summary>
    public Result Deactivate()
    {
        if (Status == TransactionStatus.Inactive)
        {
            return Result.Success();
        }

        return TransitionTo(TransactionStatus.Inactive, id => new TransactionDeactivated(id, UtcNow));
    }

    private Result TransitionTo(TransactionStatus target, Func<Guid, IDomainEvent> describe)
    {
        var current = Status;

        if (!current.CanTransitionTo(target))
        {
            return Result.Failure(DomainErrors.Transaction.IllegalTransition(current, target));
        }

        TransactionStatusId = target.Value;
        Touch();
        Raise(describe(Id));

        return Result.Success();
    }

    private void Touch() => ModifiedAtUtc = UtcNow;

    /// <summary>
    /// All timestamps are stored in UTC. A caller that hands us a local or unspecified kind
    /// gets it interpreted rather than silently stored with the wrong offset, which matters
    /// in South Africa where SAST is UTC+2. design: doc 02 section 1.4.
    /// </summary>
    private static DateTime NormaliseToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
