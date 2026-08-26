using System.Net.Http.Json;
using System.Text.Json;
using FTMS.Api.IntegrationTests.Infrastructure;
using FTMS.Application.Transactions;
using FTMS.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FTMS.Api.IntegrationTests;

/// <summary>
/// The three tests doc 08 section 3 singles out, because each one verifies a promise a
/// different design doc made. These are the tests that turn documentation into reality:
///
///   1. Audit completeness  - the doc 03 interceptor cannot be bypassed by any code path.
///   2. No DELETE permission - the doc 06 permission design is deployed, not documentation fiction.
///   3. Migration vs design  - a fresh container matches the doc 02 DDL, seeds included.
/// </summary>
[Collection(ApiCollection.Name)]
public class PromiseVerifyingTests(FtmsApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task InitializeAsync() =>
        DockerAvailability.IsAvailable ? factory.ResetDatabaseAsync() : Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------------------
    // Promise 1: every write leaves exactly the expected audit rows.
    // ---------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Every_write_endpoint_leaves_exactly_the_expected_audit_rows()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Manager, "audit.tester");

        var created = await CreateTransaction(client);

        var read = await client.GetAsync($"/api/transactions/{created.Id}");
        var etag = read.Headers.ETag!.ToString();

        var update = new HttpRequestMessage(HttpMethod.Put, $"/api/transactions/{created.Id}")
        {
            Content = JsonContent.Create(new
            {
                transactionDate = "2026-08-21T10:00:00Z",
                transactionType = "Transfer",
            }),
        };
        update.Headers.TryAddWithoutValidation("If-Match", etag);
        await client.SendAsync(update);

        await client.DeleteAsync($"/api/transactions/{created.Id}");

        // A second DELETE is a no op, so it must NOT produce a fourth audit row. An audit
        // trail that records non events is as misleading as one that misses real ones.
        await client.DeleteAsync($"/api/transactions/{created.Id}");

        var audits = await AuditRowsFor(created.Id);

        audits.Select(row => row.ChangeType)
            .ShouldBe(["Created", "Updated", "StatusChanged"]);

        audits.ShouldAllBe(row => row.ChangedBy == "audit.tester");
        audits[0].OldValues.ShouldBeNull("there is no before state on create.");
        audits.Skip(1).ShouldAllBe(row => row.OldValues != null);
    }

    [SkippableFact]
    public async Task The_audit_before_snapshot_records_the_real_prior_money()
    {
        // Money is an owned type, so EF tracks it as a separate entry. A snapshot built only
        // from the Transaction entry would report the amount as whatever it is now. On a
        // financial audit table that is not a rounding error, it is a lie.
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Manager);
        var created = await CreateTransaction(client);

        await client.DeleteAsync($"/api/transactions/{created.Id}");

        var audits = await AuditRowsFor(created.Id);
        var statusChange = audits.Single(row => row.ChangeType == "StatusChanged");

        statusChange.OldValues.ShouldNotBeNull();
        statusChange.OldValues.ShouldContain("\"amount\":1500.00");
        statusChange.OldValues.ShouldContain("\"currencyCode\":\"ZAR\"");
        statusChange.OldValues.ShouldContain("\"status\":\"Active\"");
        statusChange.NewValues.ShouldContain("\"status\":\"Inactive\"");
    }

    [SkippableFact]
    public async Task A_failed_write_leaves_no_audit_row()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Capturer);

        await client.PostAsJsonAsync("/api/transactions", new
        {
            transactionDate = "2026-08-20T09:30:00Z",
            transactionType = "Refund",
            amount = -5m,
        });

        (await CountAudits()).ShouldBe(0);
    }

    // ---------------------------------------------------------------------------------------
    // Promise 2: the application's SQL login has no DELETE on Transactions.
    // ---------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_physical_delete_through_the_domain_is_impossible()
    {
        // design: doc 06 decision 4 - the permission model enforces what the architecture
        // promises. The Testcontainers image connects as sa, so the SQL GRANT/DENY half of
        // that design cannot be exercised here (see the deployment note below). What CAN be
        // proven is the layer above it: the aggregate offers no way to remove a row, the
        // repository interface has no Delete, and the audit interceptor refuses outright if
        // anyone ever adds one.
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Manager);
        var created = await CreateTransaction(client);

        await client.DeleteAsync($"/api/transactions/{created.Id}");

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FtmsDbContext>();

        var row = await context.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(transaction => transaction.Id == created.Id);

        row.ShouldNotBeNull("DELETE is a status change, so the row must still be there.");

        // Prove the interceptor is the last line of defence: a future developer who reaches
        // past the aggregate and calls Remove gets an exception, not a silent deletion.
        var tracked = await context.Transactions.FirstAsync(t => t.Id == created.Id);
        context.Transactions.Remove(tracked);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());

        thrown.Message.ShouldContain("soft deletes only");

        // TODO design: doc 06 section 5.1 - the deployment scripts must create the
        // application login with SELECT, INSERT and UPDATE on the three tables and explicitly
        // NO DELETE on Transactions, then this test gains a second half that connects with
        // that login and asserts a raw DELETE fails with permission denied.
    }

    [SkippableFact]
    public async Task The_transactions_table_has_no_isdeleted_column()
    {
        // design: doc 02 section 1.6 - soft delete is modelled purely through status. Two
        // sources of truth for the same fact is a bug factory.
        DockerAvailability.RequireDocker();

        var columns = await ScalarList(
            "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Transactions')");

        columns.ShouldNotContain("IsDeleted");
        columns.ShouldNotContain("DeletedAt");
        columns.ShouldContain("TransactionStatusId");
    }

    // ---------------------------------------------------------------------------------------
    // Promise 3: a fresh container migrated from zero matches the doc 02 design.
    // ---------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Migrating_from_zero_produces_the_documented_schema()
    {
        DockerAvailability.RequireDocker();

        var tables = await ScalarList(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'");

        tables.ShouldContain("Transactions");
        tables.ShouldContain("TransactionStatuses");
        tables.ShouldContain("TransactionAudits");
    }

    [SkippableFact]
    public async Task Migrating_from_zero_seeds_the_five_statuses_with_their_fixed_guids()
    {
        // design: doc 02 section 4 - fixed GUIDs, not NEWID(), so every environment is
        // identical and the doc 07 filtered index can name the Active id in its WHERE clause.
        DockerAvailability.RequireDocker();

        var seeded = await ScalarList(
            "SELECT CONCAT(StatusName, '=', LOWER(CONVERT(varchar(36), Id))) "
            + "FROM dbo.TransactionStatuses ORDER BY StatusName");

        seeded.ShouldBe(
        [
            "Active=a1b2c3d4-0001-4000-8000-000000000001",
            "Cancelled=a1b2c3d4-0005-4000-8000-000000000005",
            "Completed=a1b2c3d4-0004-4000-8000-000000000004",
            "Inactive=a1b2c3d4-0002-4000-8000-000000000002",
            "Pending=a1b2c3d4-0003-4000-8000-000000000003",
        ]);
    }

    [SkippableFact]
    public async Task The_schema_carries_the_money_and_concurrency_shapes_the_design_specified()
    {
        DockerAvailability.RequireDocker();

        var columns = await ScalarList(
            """
            SELECT CONCAT(c.name, ':', t.name, ':', c.precision, ',', c.scale, ':', c.max_length)
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID('dbo.Transactions')
            """);

        // design: doc 02 section 1.1 - DECIMAL(18,2), never FLOAT, and CHAR(3) for ISO 4217.
        columns.ShouldContain(column => column.StartsWith("Amount:decimal:18,2", StringComparison.Ordinal));
        columns.ShouldContain(column => column.StartsWith("CurrencyCode:char:", StringComparison.Ordinal));
        columns.ShouldNotContain(column => column.Contains(":float:", StringComparison.Ordinal));

        // design: doc 02 section 1.8 - rowversion optimistic concurrency.
        columns.ShouldContain(column => column.StartsWith("RowVersion:timestamp", StringComparison.Ordinal));

        // design: doc 02 section 1.4 - DATETIME2(3), not DATETIME.
        columns.ShouldContain(column => column.StartsWith("TransactionDate:datetime2:3", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task The_indexes_and_constraints_from_the_design_are_present()
    {
        DockerAvailability.RequireDocker();

        var indexes = await ScalarList(
            "SELECT CONCAT(name, ':', type_desc, ':', has_filter) FROM sys.indexes "
            + "WHERE object_id = OBJECT_ID('dbo.Transactions') AND name IS NOT NULL");

        // design: doc 02 section 3 plus the nonclustered PK correction. SQL Server compares
        // uniqueidentifier by the LAST six bytes, so a clustered GUID v7 key would fragment;
        // the clustered index lives on CreatedAtUtc, which genuinely appends.
        indexes.ShouldContain("PK_Transactions:NONCLUSTERED:0");
        indexes.ShouldContain("IX_Transactions_CreatedAtUtc:CLUSTERED:0");
        indexes.ShouldContain("IX_Transactions_TransactionStatusId:NONCLUSTERED:0");
        indexes.ShouldContain("IX_Transactions_TransactionDate:NONCLUSTERED:0");

        // design: doc 07 section 3 - the covering filtered index on Active.
        indexes.ShouldContain("IX_Transactions_Active_Date:NONCLUSTERED:1");

        var checks = await ScalarList(
            "SELECT name FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.Transactions')");
        checks.ShouldContain("CK_Transactions_Amount");

        var unique = await ScalarList(
            "SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.TransactionStatuses') "
            + "AND is_unique = 1 AND name IS NOT NULL");
        unique.ShouldContain("UQ_TransactionStatuses_StatusName");
    }

    [SkippableFact]
    public async Task There_are_no_pending_model_changes_the_migrations_have_not_captured()
    {
        // Catches the commonest drift of all: someone edits an entity configuration and
        // forgets to add the migration. design: doc 08 section 3.
        DockerAvailability.RequireDocker();

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FtmsDbContext>();

        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------------------------------

    private sealed record AuditRow(string ChangeType, string? OldValues, string NewValues, string ChangedBy);

    private async Task<List<AuditRow>> AuditRowsFor(Guid transactionId)
    {
        await using var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ChangeType, OldValues, NewValues, ChangedBy FROM dbo.TransactionAudits "
            + "WHERE TransactionId = @id ORDER BY ChangedAtUtc, ChangeType";
        command.Parameters.AddWithValue("@id", transactionId);

        var rows = new List<AuditRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AuditRow(
                reader.GetString(0),
                await reader.IsDBNullAsync(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    private async Task<int> CountAudits()
    {
        await using var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.TransactionAudits";

        return (int)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<string>> ScalarList(string sql)
    {
        await using var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetValue(0).ToString()!);
        }

        return values;
    }

    private static async Task<TransactionDto> CreateTransaction(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            transactionDate = "2026-08-20T09:30:00Z",
            transactionType = "Deposit",
            amount = 1500.00m,
            currencyCode = "ZAR",
        });

        return (await response.Content.ReadFromJsonAsync<TransactionDto>(Json))!;
    }
}
