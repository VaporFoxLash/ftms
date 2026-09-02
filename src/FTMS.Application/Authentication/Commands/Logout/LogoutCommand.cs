using FTMS.Application.Abstractions;

namespace FTMS.Application.Authentication.Commands.Logout;

/// <summary>
/// Ends the current session by revoking its refresh token.
///
/// There is no validator, and that is the point: logout must succeed unconditionally. A caller
/// with an expired, malformed or entirely absent cookie is trying to end up signed out, and
/// returning them a 400 for it would be perverse. The access token they still hold expires on
/// its own within fifteen minutes - revoking the refresh token is what stops the session being
/// renewed past that. design: doc 06 section 3.
/// </summary>
public sealed record LogoutCommand(string? RefreshToken) : ICommand<Unit>;
