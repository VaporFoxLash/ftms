namespace FTMS.Application.Abstractions;

/// <summary>
/// Who is making this request. Feeds TransactionAudits.ChangedBy.
/// design: doc 02 section 1.7 and doc 06 section 6.1 - a financial audit trail that cannot
/// say who changed what is not an audit trail. Stubbed as "system" until the doc 06 Identity
/// work lands.
/// </summary>
public interface ICurrentUser
{
    /// <summary>User identifier for the audit trail. Never a token, never a password.</summary>
    string UserName { get; }
}
