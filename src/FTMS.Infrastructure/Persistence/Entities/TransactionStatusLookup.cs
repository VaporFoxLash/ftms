namespace FTMS.Infrastructure.Persistence.Entities;

/// <summary>
/// The TransactionStatuses lookup row.
///
/// design: doc 02 section 6 - the table exists because the brief requires it and because DBAs
/// and reports join against it. The domain still reasons in the TransactionStatus smart enum;
/// this type exists purely so the rows are seeded, queryable and referentially enforced.
/// Two representations of the same five facts, kept in step by the fixed GUIDs in
/// TransactionStatusIds and by the migration-versus-design DDL parity test in doc 08.
/// </summary>
public sealed class TransactionStatusLookup
{
    public TransactionStatusLookup(Guid id, string statusName)
    {
        Id = id;
        StatusName = statusName;
    }

    public Guid Id { get; private set; }

    public string StatusName { get; private set; }
}
