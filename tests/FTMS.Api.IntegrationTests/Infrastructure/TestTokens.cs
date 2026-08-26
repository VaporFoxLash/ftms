using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace FTMS.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Mints tokens for any role so the authorization matrix is testable without the login
/// ceremony. design: doc 08 section 3.
/// </summary>
public static class TestTokens
{
    public const string SigningKey = "ftms-integration-test-signing-key-at-least-32-bytes-long!!";

    public const string Capturer = "Capturer";
    public const string Manager = "Manager";
    public const string Auditor = "Auditor";
    public const string Admin = "Admin";

    public static HttpClient AuthenticatedAs(this FtmsApiFactory factory, string role, string userName = "test.user")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueFor(role, userName));

        return client;
    }

    public static string IssueFor(string role, string userName = "test.user")
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "https://ftms.tests",
            audience: "ftms-api",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userName),
                new Claim(ClaimTypes.Name, userName),
                new Claim(ClaimTypes.Role, role),
            ],
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
