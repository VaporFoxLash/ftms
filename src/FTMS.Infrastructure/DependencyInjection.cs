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

        // Both are singletons, and that is forced by AddDbContextPool below rather than chosen
        // for its own sake. A pooled DbContext reuses one DbContextOptions instance across
        // every request, so EF builds those options from the ROOT provider and simply cannot
        // resolve a scoped interceptor.
        //
        // Per request identity survives anyway: the API's ICurrentUser reads
        // IHttpContextAccessor, which is backed by AsyncLocal, so a singleton still sees the
        // current request's principal. That is precisely what the accessor exists for.
        // design: doc 07 section 4 (pooling) and doc 02 section 1.7 (ChangedBy).
        services.AddSingleton<ICurrentUser, SystemCurrentUser>();
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

        return services;
    }
}
