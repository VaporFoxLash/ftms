using FTMS.SharedKernel.Primitives;

namespace FTMS.Infrastructure.Persistence.Entities;

/// <summary>
/// One row per change to a transaction, with before and after snapshots as JSON.
///
/// design: doc 02 section 1.7 and doc 03 section 6 - this lives in Infrastructure, not in
/// Domain, on purpose. It is a persistence and compliance concern, written unconditionally by
/// the SaveChanges interceptor rather than by anything a developer has to remember to call.
/// Putting it in the domain would invite business code to write audit rows selectively, which
/// is exactly the failure mode the design rules out.
///
/// design: doc 06 section 5.3 - in production this table becomes a SQL Server 2022 append
/// only ledger table, giving cryptographic tamper evidence over the compliance trail. That is
/// applied in a follow up migration, since ledger syntax is not something EF Core emits.
/// </summary>
public sealed class TransactionAudit : Entity
{
    private TransactionAudit()
    {
        ChangeType = string.Empty;
        NewValues = string.Empty;
        ChangedBy = string.Empty;
    }

    public TransactionAudit(
        Guid transactionId,
        string changeType,
        string? oldValues,
        string newValues,
        string changedBy,
        DateTime changedAtUtc)
        : base(Guid.CreateVersion7())
    {
        TransactionId = transactionId;
        ChangeType = changeType;
        OldValues = oldValues;
        NewValues = newValues;
        ChangedBy = changedBy;
        ChangedAtUtc = changedAtUtc;
    }

    public Guid TransactionId { get; private set; }

    /// <summary>Created, Updated or StatusChanged.</summary>
    public string ChangeType { get; private set; }

    /// <summary>JSON snapshot before the change. Null on create, because there was no before.</summary>
    public string? OldValues { get; private set; }

    /// <summary>JSON snapshot after the change.</summary>
    public string NewValues { get; private set; }

    /// <summary>User or system identity. design: doc 06 - never a token, never a password.</summary>
    public string ChangedBy { get; private set; }

    public DateTime ChangedAtUtc { get; private set; }
}

/// <summary>The three change kinds the interceptor records. design: doc 02 ERD.</summary>
public static class AuditChangeTypes
{
    public const string Created = "Created";
    public const string Updated = "Updated";

    /// <summary>
    /// A status move, including the soft delete. Kept distinct from Updated because
    /// "someone corrected the date" and "someone archived this record" are different events
    /// to an auditor. design: doc 05 section 6.
    /// </summary>
    public const string StatusChanged = "StatusChanged";

    public const int MaxLength = 20;
}
