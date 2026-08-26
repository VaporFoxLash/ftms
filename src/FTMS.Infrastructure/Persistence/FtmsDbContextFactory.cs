using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FTMS.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef migrations add` build a context without booting the API.
///
/// The connection string here is used only by the EF tooling to work out the provider and to
/// generate SQL shapes; it never runs against a real database during scaffolding. The running
/// application always takes its connection string from configuration. design: doc 03.
/// </summary>
internal sealed class FtmsDbContextFactory : IDesignTimeDbContextFactory<FtmsDbContext>
{
    private const string DesignTimeConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=FtmsDesignTime;Trusted_Connection=True;TrustServerCertificate=True";

    public FtmsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FtmsDbContext>()
            // Migrations live in this same assembly, which is EF's default, so the
            // MigrationsAssembly option is deliberately not set.
            .UseSqlServer(Environment.GetEnvironmentVariable("FTMS_DESIGNTIME_CONNECTION")
                ?? DesignTimeConnectionString)
            .Options;

        return new FtmsDbContext(options);
    }
}
