using FluentValidation;
using FTMS.Application.Abstractions;

namespace FTMS.Application.Authentication.Commands.RefreshSession;

/// <summary>
/// Exchanges a refresh token for a new access token and a new refresh token.
///
/// The raw token comes from a cookie, not from a request body, so nothing a caller can type
/// reaches this command. design: doc 06 section 3.
/// </summary>
public sealed record RefreshSessionCommand(
    string RefreshToken,
    string? ClientIp = null) : ICommand<SessionResult>;

internal sealed class RefreshSessionValidator : AbstractValidator<RefreshSessionCommand>
{
    public RefreshSessionValidator()
    {
        // The controller already turns a missing cookie into AuthenticationErrors.NoRefreshToken
        // before dispatching, so this only catches a cookie that exists and is blank.
        RuleFor(command => command.RefreshToken)
            .NotEmpty()
            .WithMessage("A session cookie is required.");
    }
}
