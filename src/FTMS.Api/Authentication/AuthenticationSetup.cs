using System.Security.Claims;
using System.Text;
using FTMS.Application.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace FTMS.Api.Authentication;

/// <summary>
/// JWT bearer scaffolding.
///
/// TODO design: doc 06 section 3 - this is the seam, not the finished article. The design
/// requires ASP.NET Core Identity self hosted with its tables in our own SQL Server, 15 minute
/// access tokens with rotating one time refresh tokens revocable server side, PBKDF2 hardened
/// to current OWASP iteration counts, lockout after repeated failures, and TOTP MFA mandatory
/// for privileged roles. What is here validates tokens correctly and enforces the four role
/// policies; what is missing is the identity store and the login, refresh and MFA endpoints.
///
/// The development token endpoint below exists so the API runs end to end locally before that
/// work lands. It is registered ONLY in the Development environment.
/// </summary>
public static class AuthenticationSetup
{
    public const string DevelopmentSigningKeyPath = "Jwt:DevelopmentSigningKey";

    public static IServiceCollection AddFtmsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var signingKey = ResolveSigningKey(configuration, environment);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = configuration["Jwt:Audience"],
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateLifetime = true,

                    // No tolerance for expiry drift. A 15 minute token that works for 20 is a
                    // 20 minute token. design: doc 06 section 3.
                    ClockSkew = TimeSpan.Zero,
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.Name,
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.ReadTransactions, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(FtmsRoles.Capturer, FtmsRoles.Manager, FtmsRoles.Auditor))
            .AddPolicy(AuthorizationPolicies.WriteTransactions, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(FtmsRoles.Capturer, FtmsRoles.Manager))
            // Admin is deliberately absent from all three transaction policies. design: doc 06
            // decision 2 - separation of duty between administering the system and moving money
            // through it.
            .AddPolicy(AuthorizationPolicies.DeleteTransactions, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(FtmsRoles.Manager));

        // The audit trail's ChangedBy now comes from the authenticated principal rather than
        // the "system" placeholder Infrastructure registers. design: doc 02 section 1.7.
        //
        // Singleton, not scoped: the audit interceptor is a singleton because AddDbContextPool
        // resolves its options from the root provider, so anything the interceptor depends on
        // must be resolvable there too. Reading IHttpContextAccessor per call keeps this
        // correct, because the accessor is AsyncLocal backed.
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<ICurrentUser, HttpContextCurrentUser>());

        return services;
    }

    private static string ResolveSigningKey(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration[DevelopmentSigningKeyPath];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!environment.IsDevelopment() && configured.Contains("development", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The development JWT signing key is present outside Development. "
                    + "Configure a real key from a secret store. design: doc 06.");
            }

            return configured;
        }

        throw new InvalidOperationException(
            $"No JWT signing key configured at '{DevelopmentSigningKeyPath}'.");
    }
}

/// <summary>
/// Reads the authenticated user for the audit trail.
/// design: doc 06 section 7 - logs and audit rows carry user identifiers, never tokens and
/// never passwords, so only the name claim is read here.
/// </summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string UserName =>
        accessor.HttpContext?.User.Identity?.IsAuthenticated == true
            ? accessor.HttpContext.User.Identity.Name ?? "unknown"
            : "system";
}
