using FTMS.SharedKernel.Constants;
using FTMS.SharedKernel.Primitives;

namespace FTMS.Domain.Transactions;

/// <summary>
/// The lifecycle state of a transaction, keyed by the seeded lookup row's GUID.
/// design: doc 02 section 6 - the table exists because the brief requires it and because
/// DBAs and reports join against it; this enum exists so code never compares raw GUIDs
/// or strings. Both describe the same five rows.
/// </summary>
public sealed class TransactionStatus : SmartEnum<TransactionStatus, Guid>
{
    public static readonly TransactionStatus Active = new(nameof(Active), TransactionStatusIds.Active);
    public static readonly TransactionStatus Inactive = new(nameof(Inactive), TransactionStatusIds.Inactive);
    public static readonly TransactionStatus Pending = new(nameof(Pending), TransactionStatusIds.Pending);
    public static readonly TransactionStatus Completed = new(nameof(Completed), TransactionStatusIds.Completed);
    public static readonly TransactionStatus Cancelled = new(nameof(Cancelled), TransactionStatusIds.Cancelled);

    private TransactionStatus(string name, Guid id)
        : base(name, id)
    {
    }

    /// <summary>Maximum persisted length of StatusName, per the brief's NVARCHAR(50).</summary>
    public const int MaxNameLength = 50;

    /// <summary>
    /// The legal transitions, exactly as drawn in doc 02 section 5. This dictionary is the
    /// single source of truth: the aggregate consults it, and the unit test matrix asserts
    /// against it, so the diagram, the code and the tests cannot drift apart.
    ///
    /// Active and Pending are the working states and move freely between each other and into
    /// the outcomes. Completed and Cancelled are terminal business outcomes: the only thing
    /// left to do with them is archive. Inactive is the end of the road and DELETE is simply
    /// the transition into it.
    /// </summary>
    private static readonly IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> AllowedTransitions =
        new Dictionary<Guid, IReadOnlySet<Guid>>
        {
            [TransactionStatusIds.Active] = new HashSet<Guid>
            {
                TransactionStatusIds.Pending,
                TransactionStatusIds.Completed,
                TransactionStatusIds.Cancelled,
                TransactionStatusIds.Inactive,
            },
            [TransactionStatusIds.Pending] = new HashSet<Guid>
            {
                TransactionStatusIds.Active,
                TransactionStatusIds.Completed,
                TransactionStatusIds.Cancelled,
                TransactionStatusIds.Inactive,
            },
            [TransactionStatusIds.Completed] = new HashSet<Guid> { TransactionStatusIds.Inactive },
            [TransactionStatusIds.Cancelled] = new HashSet<Guid> { TransactionStatusIds.Inactive },
            [TransactionStatusIds.Inactive] = new HashSet<Guid>(),
        };

    /// <summary>Active and Pending. Only these permit edits to date and type.</summary>
    public bool IsWorkingState => this == Active || this == Pending;

    /// <summary>Completed, Cancelled and Inactive. These records are history.</summary>
    public bool IsHistorical => !IsWorkingState;

    /// <summary>True when moving from this status to <paramref name="target"/> is drawn in doc 02.</summary>
    public bool CanTransitionTo(TransactionStatus target) =>
        AllowedTransitions[Value].Contains(target.Value);
}
