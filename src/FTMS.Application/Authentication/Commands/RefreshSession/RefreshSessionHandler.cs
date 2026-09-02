using FTMS.Application.Abstractions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.Authentication.Commands.RefreshSession;

internal sealed class RefreshSessionHandler(
    IIdentityService identity,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenStore refreshTokens) : ICommandHandler<RefreshSessionCommand, SessionResult>
{
    public async Task<Result<SessionResult>> Handle(
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        // Rotation and replay detection both live in the store, because both need to read and
        // write the token row inside one unit of work. Doing it here would leave a window in
        // which two concurrent refreshes could each redeem the same token.
        var rotation = await refreshTokens.RotateAsync(
            command.RefreshToken,
            command.ClientIp,
            cancellationToken);

        if (!rotation.Succeeded || rotation.Replacement is null)
        {
            return Result.Failure<SessionResult>(AuthenticationErrors.InvalidRefreshToken);
        }

        // Roles are re-read rather than carried over from the old token. A user demoted five
        // minutes ago should lose the privilege at their next refresh, not when they next choose
        // to sign in. This is what makes a 15 minute access token the actual blast radius of a
        // permission change. design: doc 06 section 3.
        var user = await identity.FindByIdAsync(rotation.UserId, cancellationToken);
        if (user is null)
        {
            // The account went away while the session was live. The rotation already burned the
            // old token, so nothing further is needed to end the session.
            return Result.Failure<SessionResult>(AuthenticationErrors.UserNoLongerActive);
        }

        var access = accessTokens.Issue(user);

        return Result.Success(SessionFactory.From(user, access, rotation.Replacement));
    }
}
