namespace FTMS.Api.Authentication;

/// <summary>
/// The four roles from doc 06 section 3, with separation of duty between administering the
/// system and moving money through it.
/// </summary>
public static class FtmsRoles
{
    /// <summary>Create and update transactions.</summary>
    public const string Capturer = "Capturer";

    /// <summary>Everything Capturer can do, plus soft delete.</summary>
    public const string Manager = "Manager";

    /// <summary>Read only, including Inactive records and the audit trail.</summary>
    public const string Auditor = "Auditor";

    /// <summary>
    /// User management, and no transaction rights by default, because separating duty between
    /// administering the system and moving money through it is elementary financial control.
    /// </summary>
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Capturer, Manager, Auditor, Admin];
}

/// <summary>
/// design: doc 06 section 3 - authorization is enforced with policies on every endpoint, and
/// there are no anonymous endpoints except login and health.
/// </summary>
public static class AuthorizationPolicies
{
    public const string ReadTransactions = "transactions:read";
    public const string WriteTransactions = "transactions:write";

    /// <summary>Soft delete is a Manager act, not a Capturer one. design: doc 06 section 3.</summary>
    public const string DeleteTransactions = "transactions:delete";
}
