using FTMS.Api.Authentication;
using FTMS.Api.Contracts;
using FTMS.Application.Abstractions;
using FTMS.Application.Authentication;
using FTMS.Application.Authentication.Commands.Login;
using FTMS.Application.Authentication.Commands.Logout;
using FTMS.Application.Authentication.Commands.RefreshSession;
using FTMS.Application.Authentication.Queries.GetCurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FTMS.Api.Controllers;

/// <summary>
/// Sign in, session renewal and sign out. design: doc 06 section 3.
///
/// The split between what goes in the body and what goes in a cookie is the whole design: the
/// short lived access token is returned to script, which needs to put it in an Authorization
/// header; the long lived refresh token never touches script at all.
/// </summary>
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IDispatcher dispatcher, IWebHostEnvironment environment)
    : ApiControllerBase
{
    /// <summary>
    /// Verifies a username and password and starts a session.
    ///
    /// Anonymous, necessarily - and the only write endpoint in the API that is. It carries a
    /// strict rate limit policy for that reason. design: doc 06 section 4.
    /// </summary>
    [HttpPost("login", Name = RouteNames.Login)]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new LoginCommand(request.UserName, request.Password, ClientIp()),
            cancellationToken);

        return result.IsSuccess ? IssueSession(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Exchanges the session cookie for a new access token, and rotates the cookie.
    ///
    /// Anonymous because the caller's access token has usually already expired - that is why
    /// they are here. The cookie is the credential.
    /// </summary>
    [HttpPost("refresh", Name = RouteNames.RefreshSession)]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var cookie = RefreshTokenCookie.Read(Request);

        if (cookie is null)
        {
            // Clear anyway. A malformed or empty cookie is still a cookie the browser will keep
            // sending, and every one of those is a pointless round trip.
            RefreshTokenCookie.Clear(Response, environment.IsDevelopment());

            return Problem(AuthenticationErrors.NoRefreshToken);
        }

        var result = await dispatcher.Send(
            new RefreshSessionCommand(cookie, ClientIp()),
            cancellationToken);

        if (result.IsFailure)
        {
            // The token is spent, revoked or forged. Whichever it was, the browser should stop
            // presenting it.
            RefreshTokenCookie.Clear(Response, environment.IsDevelopment());

            return Problem(result.Error);
        }

        return IssueSession(result.Value);
    }

    /// <summary>
    /// Ends the session by revoking its refresh token.
    ///
    /// Requires authentication so that logout cannot be used as an unauthenticated probe for
    /// whether a given token value is live. Always returns 204: a caller trying to sign out
    /// should never be told they failed to.
    /// </summary>
    [HttpPost("logout", Name = RouteNames.Logout)]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new LogoutCommand(RefreshTokenCookie.Read(Request)),
            cancellationToken);

        RefreshTokenCookie.Clear(Response, environment.IsDevelopment());

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>
    /// Who the caller is, according to the identity store rather than according to their token.
    /// Lets the SPA rebuild its shell after a reload without decoding the JWT itself.
    /// </summary>
    [HttpGet("me", Name = RouteNames.GetCurrentUser)]
    [Authorize]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await dispatcher.Ask(new GetCurrentUserQuery(), cancellationToken);

        return result.IsSuccess
            ? Ok(new CurrentUserResponse(result.Value.UserName, result.Value.DisplayName, result.Value.Roles))
            : Problem(result.Error);
    }

    /// <summary>
    /// Splits the session in two: the refresh token into an httpOnly cookie, everything else into
    /// the response body. One place does this, so a new field can never accidentally be added to
    /// the body half when it belonged in the cookie half.
    /// </summary>
    private IActionResult IssueSession(SessionResult session)
    {
        RefreshTokenCookie.Write(
            Response,
            session.RefreshToken,
            session.RefreshExpiresAtUtc,
            environment.IsDevelopment());

        return Ok(new SessionResponse(
            session.AccessToken,
            session.ExpiresInSeconds,
            session.UserName,
            session.DisplayName,
            session.Roles));
    }

    /// <summary>
    /// Recorded against the refresh token for incident response. Never used for an authorization
    /// decision - it is trivially spoofable behind a proxy, and treating it as identity would be
    /// a vulnerability rather than a feature.
    /// </summary>
    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
