namespace FTMS.SharedKernel.Constants;

/// <summary>
/// The four roles from doc 06 section 3, with separation of duty between administering the
/// system and moving money through it.
///
/// These live in SharedKernel rather than the API layer because three rings need them: the API
/// builds authorization policies from them, Infrastructure seeds them into AspNetRoles, and the
/// tests assert the matrix. Names only, no behaviour - which is what SharedKernel is for.
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
