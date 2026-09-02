namespace FTMS.Api.Authentication;

/// <summary>
/// Named rate limiting policies. design: doc 06 section 4.
///
/// Only endpoints that need something stricter than the global sliding window name a policy
/// here. An endpoint that asks for a policy which was never registered throws at REQUEST time
/// rather than at startup, so every constant in this class must have a matching
/// AddPolicy call in Program.cs.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// The sign in and refresh endpoints. Far tighter than the global limit, because these are
    /// the only anonymous endpoints that accept a credential, and credential stuffing is a
    /// volume attack: it works by trying thousands of passwords cheaply. Identity's account
    /// lockout handles the single account case; this handles the spray across many accounts,
    /// which lockout cannot see.
    /// </summary>
    public const string Authentication = "auth";
}
