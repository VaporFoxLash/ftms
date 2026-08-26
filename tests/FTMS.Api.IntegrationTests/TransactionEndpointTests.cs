using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FTMS.Api.IntegrationTests.Infrastructure;
using FTMS.Application.Transactions;
using FTMS.Application.TransactionStatuses;

namespace FTMS.Api.IntegrationTests;

/// <summary>
/// The full doc 05 contract, per endpoint, against a real SQL Server.
/// design: doc 08 section 3.
/// </summary>
[Collection(ApiCollection.Name)]
public class TransactionEndpointTests(FtmsApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task InitializeAsync() =>
        DockerAvailability.IsAvailable ? factory.ResetDatabaseAsync() : Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Statuses_returns_the_five_seeded_rows()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Auditor);

        var statuses = await client.GetFromJsonAsync<List<TransactionStatusDto>>(
            "/api/transactionstatuses", Json);

        statuses.ShouldNotBeNull();
        statuses.Count.ShouldBe(5);
        statuses.Select(status => status.StatusName)
            .ShouldBe(["Active", "Cancelled", "Completed", "Inactive", "Pending"], ignoreOrder: true);
    }

    [SkippableFact]
    public async Task Anonymous_requests_are_rejected()
    {
        DockerAvailability.RequireDocker();
        // design: doc 06 section 3 - no anonymous endpoints except login and health.
        var response = await factory.CreateClient().GetAsync("/api/transactions");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Health_is_anonymous_and_reports_the_database()
    {
        DockerAvailability.RequireDocker();
        var response = await factory.CreateClient().GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Create_returns_201_with_location_and_etag()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Capturer);

        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            transactionDate = "2026-08-20T09:30:00Z",
            transactionType = "Deposit",
            amount = 1500.00m,
            currencyCode = "ZAR",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.ETag.ShouldNotBeNull();

        var created = await response.Content.ReadFromJsonAsync<TransactionDto>(Json);
        created.ShouldNotBeNull();
        created.Status.ShouldBe("Active", "the brief says a new transaction starts Active.");
        created.CurrencyCode.ShouldBe("ZAR");
        created.Amount.ShouldBe(1500.00m);
        created.ModifiedAtUtc.ShouldBeNull();
    }

    [SkippableFact]
    public async Task Created_timestamps_carry_the_utc_designator()
    {
        DockerAvailability.RequireDocker();
        // design: doc 05 section 1 - timestamps in UTC ISO 8601. Without the trailing Z a
        // browser parses the value as local time, which is two hours wrong in SAST.
        var client = factory.AuthenticatedAs(TestTokens.Capturer);
        var created = await CreateTransaction(client);

        var raw = await client.GetStringAsync($"/api/transactions/{created.Id}");

        // Assert on the raw JSON strings, not on parsed DateTimes: the whole point is the
        // wire format, and a DateTime deserialised in .NET would hide a missing Z.
        using var document = JsonDocument.Parse(raw);

        document.RootElement.GetProperty("transactionDate").GetString()
            .ShouldBe("2026-08-20T09:30:00.000Z");
        document.RootElement.GetProperty("createdAtUtc").GetString()!
            .ShouldEndWith("Z", Case.Sensitive);
    }

    [SkippableFact]
    public async Task Currency_defaults_to_zar_when_omitted()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Capturer);

        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            transactionDate = "2026-08-20T09:30:00Z",
            transactionType = "Payment",
            amount = 10m,
        });

        var created = await response.Content.ReadFromJsonAsync<TransactionDto>(Json);
        created!.CurrencyCode.ShouldBe("ZAR");
    }

    [SkippableTheory]
    [InlineData("Refund", 100, "transactionType")]
    [InlineData("Deposit", -1, "amount")]
    [InlineData("Deposit", 0, "amount")]
    public async Task Invalid_creates_return_400_with_a_field_keyed_errors_dictionary(
        string type,
        decimal amount,
        string expectedField)
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Capturer);

        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            transactionDate = "2026-08-20T09:30:00Z",
            transactionType = type,
            amount,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain($"\"{expectedField}\"");
    }

    [SkippableFact]
    public async Task List_defaults_to_active_and_returns_a_paging_envelope()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Capturer);
        await CreateTransaction(client);

        var page = await client.GetFromJsonAsync<PagedResult<TransactionDto>>("/api/transactions", Json);

        page.ShouldNotBeNull();
        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(50);
        page.TotalCount.ShouldBe(1);
        page.TotalPages.ShouldBe(1);
        page.Items.ShouldAllBe(item => item.Status == "Active");
    }

    [SkippableFact]
    public async Task Page_size_is_capped_at_two_hundred()
    {
        DockerAvailability.RequireDocker();
        // design: doc 05 section 3 - pageSize is capped at 200 server side.
        var client = factory.AuthenticatedAs(TestTokens.Capturer);

        var page = await client.GetFromJsonAsync<PagedResult<TransactionDto>>(
            "/api/transactions?pageSize=5000", Json);

        page!.PageSize.ShouldBe(200);
    }

    [SkippableFact]
    public async Task An_unknown_status_fails_loudly_rather_than_returning_an_empty_list()
    {
        DockerAvailability.RequireDocker();
        // design: doc 05 section 3 - typos fail loudly.
        var client = factory.AuthenticatedAs(TestTokens.Capturer);

        var response = await client.GetAsync("/api/transactions?status=Actve");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Get_by_id_returns_a_soft_deleted_transaction_because_it_is_the_audit_window()
    {
        DockerAvailability.RequireDocker();
        // design: doc 05 section 4 and decision 2 - hiding soft deleted rows from this
        // endpoint would defeat the reason we soft delete.
        var client = factory.AuthenticatedAs(TestTokens.Manager);
        var created = await CreateTransaction(client);

        (await client.DeleteAsync($"/api/transactions/{created.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var fetched = await client.GetFromJsonAsync<TransactionDto>(
            $"/api/transactions/{created.Id}", Json);

        fetched.ShouldNotBeNull();
        fetched.Status.ShouldBe("Inactive");
    }

    [SkippableFact]
    public async Task Get_by_id_honours_if_none_match_with_304()
    {
        DockerAvailability.RequireDocker();
        // design: doc 07 section 4 - the response says when nothing changed and the body
        // never travels.
        var client = factory.AuthenticatedAs(TestTokens.Capturer);
        var created = await CreateTransaction(client);

        var first = await client.GetAsync($"/api/transactions/{created.Id}");
        var etag = first.Headers.ETag!.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/transactions/{created.Id}");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var second = await client.SendAsync(request);

        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        (await second.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [SkippableFact]
    public async Task Get_by_id_returns_404_for_an_id_that_never_existed()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Auditor);

        var response = await client.GetAsync($"/api/transactions/{Guid.CreateVersion7()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    private async Task<TransactionDto> CreateTransaction(HttpClient client, string type = "Deposit")
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            transactionDate = "2026-08-20T09:30:00Z",
            transactionType = type,
            amount = 1500.00m,
            currencyCode = "ZAR",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<TransactionDto>(Json))!;
    }
}

