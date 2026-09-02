using FTMS.Application.Abstractions;
using FTMS.Domain.Transactions;
using FTMS.Infrastructure.Caching;
using FTMS.Infrastructure.Identity;
using FTMS.Infrastructure.Persistence;
using FTMS.Infrastructure.Persistence.Interceptors;
using FTMS.Infrastructure.Persistence.Repositories;
using FTMS.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FTMS.Infrastructure;

/// <summary>
/// Infrastructure's contribution to the composition root. design: doc 03 section 1 - the
/// Application layer declares what it needs, Infrastructure supplies the implementations, and
/// dependency injection wires them together here.
/// </summary>
public static class DependencyInjection
{
    public const string ConnectionStringName = "FtmsDatabase";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");

        // Singleton, and that is forced by AddDbContextPool below rather than chosen for its own
        // sake. A pooled DbContext reuses one DbContextOptions instance across every request, so
        // EF builds those options from the ROOT provider and simply cannot resolve a scoped
        // interceptor.
        //
        // Per request identity survives anyway: the API's ICurrentUser reads
        // IHttpContextAccessor, which is backed by AsyncLocal, so a singleton still sees the
        // current request's principal. That is precisely what the accessor exists for.
        // design: doc 07 section 4 (pooling) and doc 02 section 1.7 (ChangedBy).
        //
        // ICurrentUser itself is deliberately NOT registered here. It used to be, with the API
        // calling services.Replace to swap in the HTTP aware implementation - which quietly made
        // correctness depend on AddFtmsAuthentication being called after AddInfrastructure. One
        // registration in one place has no order to get wrong. The API layer owns it, because
        // only the API knows what a request principal is.
        services.AddSingleton<AuditSaveChangesInterceptor>();

        // design: doc 07 section 4 - DbContext pooling, because creating a context per request
        // is measurable overhead on a machine limited to the four cores Express is allowed.
        services.AddDbContextPool<FtmsDbContext>((provider, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
                // Transient SQL failures on a single host are rare but real. Retry rather than
                // surface a 500 for a connection that would have worked a moment later.
                // Migrations live in this same assembly, EF's default, so MigrationsAssembly
                // is deliberately not set.
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null));

            // The audit interceptor is attached to the context itself, not called by handlers,
            // which is precisely what makes the compliance trail unconditional.
            // design: doc 03 section 6.
            options.AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<FtmsDbContext>());
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ITransactionReadStore, TransactionReadStore>();

        services.AddMemoryCache();
        services.AddSingleton<ICacheService, MemoryCacheService>();

        services.AddFtmsIdentity(configuration);

        return services;
    }

    /// <summary>
    /// ASP.NET Core Identity, self hosted, with its stores in our own SQL Server.
    /// design: doc 06 section 3.
    /// </summary>
    private static IServiceCollection AddFtmsIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // AddIdentityCore rather than AddIdentity. The full AddIdentity registers a cookie
        // authentication scheme and makes it the default, which would silently displace JWT
        // bearer as this API's default scheme. We want the user store, the password hasher and
        // the role manager; we do not want cookie sign in.
        services
            .AddIdentityCore<FtmsUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Length does more for password strength than character class rules do, so the
                // floor is high and the composition requirements are mild. Requiring a symbol as
                // well mostly produces "Password1!" and a sticky note.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                // design: doc 06 section 4 - lockout blunts credential stuffing at the account
                // level, the login rate limiter blunts it at the network level, and neither is
                // sufficient alone: lockout alone lets an attacker deny service to a known user,
                // rate limiting alone lets a slow distributed attack through.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<FtmsRole>()
            .AddEntityFrameworkStores<FtmsDbContext>();

        // Validated explicitly rather than through ValidateDataAnnotations, which would mean
        // taking Microsoft.Extensions.Options.DataAnnotations for four rules. ValidateOnStart is
        // the part that matters: a deployment with a missing or weak signing key fails at
        // startup, loudly, instead of at the first sign in attempt.
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Issuer),
                "Jwt:Issuer is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Audience),
                "Jwt:Audience is required.")
            .Validate(
                options => System.Text.Encoding.UTF8.GetByteCount(options.SigningKey)
                    >= JwtOptions.MinimumSigningKeyBytes,
                $"Jwt:SigningKey is required and must be at least "
                    + $"{JwtOptions.MinimumSigningKeyBytes} bytes.")
            .Validate(
                options => options.AccessTokenMinutes is > 0 and <= 60,
                "Jwt:AccessTokenMinutes must be between 1 and 60.")
            .Validate(
                options => options.RefreshTokenDays is > 0 and <= 90,
                "Jwt:RefreshTokenDays must be between 1 and 90.")
            .ValidateOnStart();

        services
            .AddOptions<IdentitySeedOptions>()
            .Bind(configuration.GetSection(IdentitySeedOptions.SectionName));

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();

        // Singleton: it holds one SigningCredentials and no per request state.
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        return services;
    }
}
