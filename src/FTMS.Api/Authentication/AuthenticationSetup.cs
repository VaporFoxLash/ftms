using System.Security.Claims;
using System.Text;
using FTMS.Application.Abstractions;
using FTMS.Infrastructure.Identity;
using FTMS.SharedKernel.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace FTMS.Api.Authentication;

/// <summary>
/// JWT bearer validation and the role policies.
///
/// design: doc 06 section 3. The identity store, the password hashing and the token issuing all
/// live in Infrastructure (see AddInfrastructure); what remains here is the part that is
/// genuinely about HTTP - validating the bearer token on the way in, and mapping roles onto
/// endpoint policies.
/// </summary>
public static class AuthenticationSetup
{
    public const string SigningKeyPath = "Jwt:SigningKey";

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

        // The audit trail's ChangedBy comes from the authenticated principal.
        // design: doc 02 section 1.7.
        //
        // Singleton, not scoped: the audit interceptor is a singleton because AddDbContextPool
        // resolves its options from the root provider, so anything the interceptor depends on
        // must be resolvable there too. Reading IHttpContextAccessor per call keeps this
        // correct, because the accessor is AsyncLocal backed.
        //
        // This is the ONLY registration of ICurrentUser in the application. Infrastructure used
        // to register a "system" placeholder that this line then had to services.Replace, which
        // made the audit trail's correctness depend on the order these two extension methods
        // were called in. One registration cannot be ordered wrongly.
        services.AddHttpContextAccessor();
        services.AddSingleton<ICurrentUser, HttpContextCurrentUser>();

        return services;
    }

    /// <summary>
    /// Reads the signing key, and refuses to start rather than run on a weak or public one.
    ///
    /// The previous guard searched the configured value for the substring "development", which
    /// was a heuristic pretending to be a control: it passed anything that did not happen to
    /// contain that word, including an empty-ish key or the dev key with one letter changed.
    /// This one checks the two things that actually matter - that the key is long enough to be
    /// a real HMAC-SHA256 key, and that it is not the specific value committed to this
    /// repository. design: doc 06 section 3.
    /// </summary>
    private static string ResolveSigningKey(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration[SigningKeyPath];

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"No JWT signing key configured at '{SigningKeyPath}'. In Development this comes "
                + "from appsettings.Development.json; elsewhere it must come from a secret store.");
        }

        if (Encoding.UTF8.GetByteCount(configured) < JwtOptions.MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"The JWT signing key at '{SigningKeyPath}' is shorter than "
                + $"{JwtOptions.MinimumSigningKeyBytes} bytes. HMAC-SHA256 gains nothing from a "
                + "key shorter than its own digest, and loses security margin.");
        }

        if (!environment.IsDevelopment()
            && string.Equals(configured, JwtOptions.KnownDevelopmentKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The development JWT signing key is committed to source control and is therefore "
                + "public. Configure a real key from a secret store. design: doc 06.");
        }

        return configured;
    }
}

/// <summary>
/// Reads the authenticated user for the audit trail.
/// design: doc 06 section 7 - logs and audit rows carry user identifiers, never tokens and
/// never passwords, so only the name claim is read here.
/// </summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <summary>
    /// What the audit trail records when no principal is attached - a migration on startup, or a
    /// background task. Honest about what we know rather than inventing a user.
    /// </summary>
    public const string SystemIdentity = "system";

    private const string UnknownIdentity = "unknown";

    public string UserName =>
        accessor.HttpContext?.User.Identity?.IsAuthenticated == true
            ? accessor.HttpContext.User.Identity.Name ?? UnknownIdentity
            : SystemIdentity;
}
