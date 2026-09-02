using FTMS.Application.Abstractions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.Authentication.Commands.Login;

internal sealed class LoginHandler(
    IIdentityService identity,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenStore refreshTokens) : ICommandHandler<LoginCommand, SessionResult>
{
    public async Task<Result<SessionResult>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var check = await identity.CheckCredentialsAsync(
            command.UserName,
            command.Password,
            cancellationToken);

        if (check == CredentialCheck.LockedOut)
        {
            return Result.Failure<SessionResult>(AuthenticationErrors.AccountLocked);
        }

        if (check != CredentialCheck.Succeeded)
        {
            return Result.Failure<SessionResult>(AuthenticationErrors.InvalidCredentials);
        }

        var user = await identity.FindByNameAsync(command.UserName, cancellationToken);
        if (user is null)
        {
            // The credential check just passed, so this can only mean the account was deleted
            // between the two calls. Vanishingly rare, but returning the generic credential
            // error rather than throwing keeps a race from surfacing as a 500.
            return Result.Failure<SessionResult>(AuthenticationErrors.InvalidCredentials);
        }

        var access = accessTokens.Issue(user);
        var refresh = await refreshTokens.IssueAsync(user.UserId, command.ClientIp, cancellationToken);

        return Result.Success(SessionFactory.From(user, access, refresh));
    }
}
