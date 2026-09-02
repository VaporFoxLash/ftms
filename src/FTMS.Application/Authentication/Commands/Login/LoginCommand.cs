using FluentValidation;
using FTMS.Application.Abstractions;

namespace FTMS.Application.Authentication.Commands.Login;

/// <summary>
/// design: doc 06 section 3 - username and password, verified against the identity store.
/// <paramref name="ClientIp"/> is not a client supplied field; the controller reads it from the
/// connection and passes it through for the refresh token's audit column.
/// </summary>
public sealed record LoginCommand(
    string UserName,
    string Password,
    string? ClientIp = null) : ICommand<SessionResult>;

/// <summary>
/// Presence checks only.
///
/// There is deliberately no password complexity rule here. Complexity is enforced when a
/// password is SET, by Identity's own PasswordOptions; enforcing it again at sign in would tell
/// an attacker which of their guesses were even eligible to be somebody's password, and would
/// lock existing users out of their accounts the day the policy tightened.
/// </summary>
internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(command => command.UserName)
            .NotEmpty()
            .WithMessage("Username is required.");

        RuleFor(command => command.Password)
            .NotEmpty()
            .WithMessage("Password is required.");
    }
}
