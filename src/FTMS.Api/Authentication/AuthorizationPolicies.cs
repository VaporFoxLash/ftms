namespace FTMS.Api.Authentication;

/// <summary>
/// design: doc 06 section 3 - authorization is enforced with policies on every endpoint, and
/// there are no anonymous endpoints except login and health.
///
/// The role names these policies are built from live in
/// <see cref="FTMS.SharedKernel.Constants.FtmsRoles"/>, because Infrastructure seeds the same
/// names into AspNetRoles and the two must not be able to drift.
/// </summary>
public static class AuthorizationPolicies
{
    public const string ReadTransactions = "transactions:read";
    public const string WriteTransactions = "transactions:write";

    /// <summary>Soft delete is a Manager act, not a Capturer one. design: doc 06 section 3.</summary>
    public const string DeleteTransactions = "transactions:delete";
}
