using FTMS.Application.Abstractions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.Authentication.Commands.Logout;

internal sealed class LogoutHandler(IRefreshTokenStore refreshTokens)
    : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Result<Unit>> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            // Idempotent in the store: revoking an unknown or already dead token is a success.
            await refreshTokens.RevokeAsync(command.RefreshToken, cancellationToken);
        }

        return Result.Success(Unit.Value);
    }
}
