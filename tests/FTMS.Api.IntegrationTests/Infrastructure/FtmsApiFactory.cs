using System.Data.Common;
using FTMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Respawn;
using Testcontainers.MsSql;

namespace FTMS.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API pipeline against a genuine SQL Server in a container.
///
/// design: doc 08 section 3 - WebApplicationFactory hosting the real API pipeline,
/// Testcontainers spinning up a real SQL Server per test run, Respawn resetting data between
/// tests. Nothing is mocked below the HTTP boundary, which is the point: these tests exist to
/// prove that promises other docs made are deployed reality rather than documentation fiction.
/// </summary>
public sealed class FtmsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// SQL Server 2022, matching production, because doc 06 section 5.3 puts the audit table
    /// on 2022 ledger tables and doc 08 will not accept a substitute engine.
    /// </summary>
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>
    /// Built in InitializeAsync rather than in a field initializer, and that matters.
    /// Constructing an MsSqlBuilder touches TestcontainersSettings, whose static constructor
    /// throws outright when no Docker endpoint is configured. A field initializer runs during
    /// fixture construction, which is before any test level Skip can fire, so the whole
    /// collection would fail on a machine without Docker instead of skipping.
    /// </summary>
    private MsSqlContainer? _container;

    private Respawner? _respawner;
    private DbConnection? _resetConnection;

    public string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException(DockerAvailability.SkipReason);

    public async Task InitializeAsync()
    {
        // A collection fixture runs before any test, so it cannot be skipped by a test level
        // guard. Without this check, a machine with no Docker fails the entire collection
        // during setup and never reaches the Skip.IfNot in each test.
        if (!DockerAvailability.IsAvailable)
        {
            return;
        }

        _container = new MsSqlBuilder(SqlServerImage).Build();
        await _container.StartAsync();

        // design: doc 08 section 3 - the migration test. A fresh container migrated from zero
        // must produce the doc 02 schema, seeds included, so drift between the migrations and
        // the design is caught the day it happens.
        await using (var scope = Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<FtmsDbContext>().Database.MigrateAsync();
        }

        _resetConnection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await _resetConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_resetConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,

            // The seeded statuses are migration output, not test data. Wiping them would break
            // every foreign key and force each test to reseed. design: doc 02 section 4.
            TablesToIgnore = ["TransactionStatuses", "__EFMigrationsHistory"],
        });
    }

    /// <summary>Empties the transaction and audit tables between tests, leaving the seeds alone.</summary>
    public async Task ResetDatabaseAsync()
    {
        if (_respawner is not null && _resetConnection is not null)
        {
            await _respawner.ResetAsync(_resetConnection);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // The placeholder is never used: without Docker every test skips before it
                // can build a client. It only keeps host construction from throwing during
                // teardown on a Docker-less machine.
                ["ConnectionStrings:FtmsDatabase"] = _container?.GetConnectionString()
                    ?? "Server=(unavailable);Database=Ftms;Trusted_Connection=True",
                ["Jwt:Issuer"] = "https://ftms.tests",
                ["Jwt:Audience"] = "ftms-api",
                ["Jwt:DevelopmentSigningKey"] = TestTokens.SigningKey,
            }));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_resetConnection is not null)
        {
            await _resetConnection.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}

/// <summary>
/// One container for the whole assembly. Starting SQL Server costs tens of seconds; doing it
/// per test class would make the suite unusable and tempt people into deleting it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<FtmsApiFactory>
{
    public const string Name = "ftms-api";
}
