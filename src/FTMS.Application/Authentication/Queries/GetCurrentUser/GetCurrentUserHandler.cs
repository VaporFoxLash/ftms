using FTMS.Application.Abstractions;
using FTMS.SharedKernel.Results;

namespace FTMS.Application.Authentication.Queries.GetCurrentUser;

internal sealed class GetCurrentUserHandler(
    ICurrentUser currentUser,
    IIdentityService identity) : IQueryHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async Task<Result<CurrentUserDto>> Handle(
        GetCurrentUserQuery query,
        CancellationToken cancellationToken)
    {
        var user = await identity.FindByNameAsync(currentUser.UserName, cancellationToken);

        if (user is null)
        {
            // The endpoint is behind [Authorize], so the token validated - meaning the account
            // was removed while a live token was still in circulation. Reporting it as no longer
            // active tells the SPA to sign out, which is exactly right.
            return Result.Failure<CurrentUserDto>(AuthenticationErrors.UserNoLongerActive);
        }

        return Result.Success(new CurrentUserDto(user.UserName, user.DisplayName, user.Roles));
    }
}
