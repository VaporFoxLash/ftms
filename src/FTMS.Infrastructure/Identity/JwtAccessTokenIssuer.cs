using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FTMS.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FTMS.Infrastructure.Identity;

/// <summary>
/// Mints the short lived bearer token. design: doc 06 section 3.
///
/// The signing credentials are built once and reused. <see cref="SymmetricSecurityKey"/> is
/// thread safe and the key never changes for the life of the process, so rebuilding it per token
/// would be pure allocation.
/// </summary>
internal sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly JwtOptions options;
    private readonly SigningCredentials credentials;
    private readonly JwtSecurityTokenHandler handler = new();

    public JwtAccessTokenIssuer(IOptions<JwtOptions> options)
    {
        this.options = options.Value;

        this.credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this.options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public AccessToken Issue(AuthenticatedUser user)
    {
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),

            // ClaimTypes.Name, not the "name" JWT claim, because AuthenticationSetup configures
            // NameClaimType = ClaimTypes.Name. This is the value HttpContextCurrentUser reads
            // and the audit trail stamps into ChangedBy. design: doc 02 section 1.7.
            new(ClaimTypes.Name, user.UserName),

            // A unique token id. Not checked against a denylist today - the fifteen minute
            // lifetime is the revocation story for access tokens - but present so that adding
            // one later does not require reissuing every token in circulation.
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };

        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: credentials);

        return new AccessToken(handler.WriteToken(token), expiresAt);
    }
}
