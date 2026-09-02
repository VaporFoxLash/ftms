using FTMS.Application.Abstractions;

namespace FTMS.Application.Authentication.Queries.GetCurrentUser;

/// <summary>
/// Who is calling, according to the identity store rather than according to the token.
///
/// Deliberately NOT an <see cref="ICachedQuery"/>. Caching the answer would mean a role change
/// kept taking effect late even after the user refreshed, which is the exact staleness the short
/// access token lifetime exists to bound. design: doc 03 section 7.
/// </summary>
public sealed record GetCurrentUserQuery : IQuery<CurrentUserDto>;
