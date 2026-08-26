using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace FTMS.Api.Authentication;

/// <summary>
/// A development only token issuer, so the API and the Angular client run end to end before
/// the real login lands.
///
/// TODO design: doc 06 section 3 - this is NOT authentication. It verifies nothing and trusts
/// whatever role it is asked for. It is registered only when the environment is Development,
/// and it must be deleted the moment ASP.NET Core Identity ships with real login, rotating one
/// time refresh tokens and TOTP MFA.
/// </summary>
public static class DevelopmentTokenEndpoint
{
    public sealed record DevelopmentTokenRequest(string? UserName, string[]? Roles);

    public sealed record DevelopmentTokenResponse(string AccessToken, string TokenType, int ExpiresInSeconds);

    public static IEndpointRouteBuilder MapDevelopmentTokenEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/dev/token", (
            DevelopmentTokenRequest? request,
            IConfiguration configuration) =>
        {
            var userName = string.IsNullOrWhiteSpace(request?.UserName) ? "dev.user" : request.UserName;

            // Manager by default, because that role can do everything the five endpoints offer.
            var roles = request?.Roles is { Length: > 0 } requested
                ? requested.Where(FtmsRoles.All.Contains).ToArray()
                : [FtmsRoles.Manager];

            if (roles.Length == 0)
            {
                return Results.BadRequest(new
                {
                    error = $"Roles must be drawn from: {string.Join(", ", FtmsRoles.All)}.",
                });
            }

            var minutes = configuration.GetValue("Jwt:AccessTokenMinutes", 15);
            var signingKey = configuration[AuthenticationSetup.DevelopmentSigningKeyPath]
                ?? throw new InvalidOperationException("No development signing key configured.");

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, userName),
                new(ClaimTypes.Name, userName),
                new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            };

            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var token = new JwtSecurityToken(
                issuer: configuration["Jwt:Issuer"],
                audience: configuration["Jwt:Audience"],
                claims: claims,
                notBefore: DateTime.UtcNow,

                // 15 minutes, matching the real token lifetime from doc 06 section 3, so
                // clients are built against the refresh cadence they will actually face.
                expires: DateTime.UtcNow.AddMinutes(minutes),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    SecurityAlgorithms.HmacSha256));

            return Results.Ok(new DevelopmentTokenResponse(
                new JwtSecurityTokenHandler().WriteToken(token),
                "Bearer",
                minutes * 60));
        })
        .AllowAnonymous()
        .WithName("IssueDevelopmentToken")
        .WithSummary("Development only. Mints a JWT for the requested roles without verifying anything.");

        return endpoints;
    }
}
