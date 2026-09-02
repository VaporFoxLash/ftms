using FTMS.Application.Abstractions;

namespace FTMS.Application.Authentication;

/// <summary>
/// Assembles a <see cref="SessionResult"/> from its two halves.
///
/// One function rather than two nearly identical projections in the login and refresh handlers.
/// A refreshed session and a freshly signed in one must be indistinguishable once issued: if the
/// two paths built the record separately, a field added to one would silently go missing from
/// the other, and the bug would only show up for users whose access token had expired.
/// </summary>
internal static class SessionFactory
{
    public static SessionResult From(AuthenticatedUser user, AccessToken access, IssuedRefreshToken refresh) =>
        new(
            access.Value,
            access.ExpiresInSeconds,
            user.UserName,
            user.DisplayName,
            user.Roles,
            refresh.RawToken,
            refresh.ExpiresAtUtc);
}
