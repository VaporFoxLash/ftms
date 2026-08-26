using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FTMS.Api.IntegrationTests.Infrastructure;
using FTMS.Application.Transactions;

namespace FTMS.Api.IntegrationTests;

/// <summary>
/// The ETag dance and idempotent soft delete, against a real rowversion column.
/// design: doc 08 section 3 - the ETag dance (428 without If-Match, 412 on stale, success on
/// fresh) and idempotent DELETE returning 204 twice.
/// </summary>
[Collection(ApiCollection.Name)]
public class ConcurrencyAndDeletionTests(FtmsApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task InitializeAsync() =>
        DockerAvailability.IsAvailable ? factory.ResetDatabaseAsync() : Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Update_without_if_match_is_428()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Capturer);
        var created = await CreateTransaction(client);

        var response = await client.PutAsJsonAsync($"/api/transactions/{created.Id}", new
        {
            transactionDate = "2026-08-21T10:00:00Z",
            transactionType = "Transfer",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
    }

    [SkippableFact]
    public async Task Update_with_a_stale_if_match_is_412_and_changes_nothing()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Capturer);
        var created = await CreateTransaction(client);

        var response = await Put(client, created.Id, "\"AAAAAAAAAAE=\"", "Transfer");

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        var unchanged = await client.GetFromJsonAsync<TransactionDto>(
            $"/api/transactions/{created.Id}", Json);
        unchanged!.TransactionType.ShouldBe("Deposit");
        unchanged.ModifiedAtUtc.ShouldBeNull("a refused update must not stamp the record.");
    }

    [SkippableFact]
    public async Task Update_with_a_fresh_if_match_succeeds_and_the_etag_moves_on()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Capturer);
        var created = await CreateTransaction(client);

        var read = await client.GetAsync($"/api/transactions/{created.Id}");
        var etag = read.Headers.ETag!.ToString();

        var response = await Put(client, created.Id, etag, "Transfer");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.ToString().ShouldNotBe(etag, "the rowversion advances on every write.");

        var updated = await response.Content.ReadFromJsonAsync<TransactionDto>(Json);
        updated!.TransactionType.ShouldBe("Transfer");
        updated.ModifiedAtUtc.ShouldNotBeNull();
        updated.Amount.ShouldBe(1500.00m, "PUT cannot touch the amount, it is not on the DTO.");
    }

    [SkippableFact]
    public async Task Replaying_the_same_etag_twice_is_refused_the_second_time()
    {
        // The heart of optimistic concurrency: the first writer wins, the second is told to
        // refetch. design: doc 05 section 6.
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Capturer);
        var created = await CreateTransaction(client);

        var read = await client.GetAsync($"/api/transactions/{created.Id}");
        var etag = read.Headers.ETag!.ToString();

        (await Put(client, created.Id, etag, "Transfer")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Put(client, created.Id, etag, "Payment")).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [SkippableFact]
    public async Task Editing_a_historical_record_is_409_not_412()
    {
        // design: doc 05 section 6 - Completed, Cancelled and Inactive records are history,
        // and history does not get edited. A fresh ETag does not buy you the right to edit one.
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Manager);
        var created = await CreateTransaction(client);

        (await client.DeleteAsync($"/api/transactions/{created.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var read = await client.GetAsync($"/api/transactions/{created.Id}");
        var freshEtag = read.Headers.ETag!.ToString();

        var response = await Put(client, created.Id, freshEtag, "Transfer");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [SkippableFact]
    public async Task Delete_is_idempotent_and_never_removes_the_row()
    {
        // design: doc 05 section 7 and decision 6.
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Manager);
        var created = await CreateTransaction(client);

        (await client.DeleteAsync($"/api/transactions/{created.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/transactions/{created.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var survivor = await client.GetFromJsonAsync<TransactionDto>(
            $"/api/transactions/{created.Id}", Json);

        survivor.ShouldNotBeNull("the row is archived, never removed.");
        survivor.Status.ShouldBe("Inactive");
    }

    [SkippableFact]
    public async Task Delete_removes_the_transaction_from_the_active_list_only()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Manager);
        var created = await CreateTransaction(client);

        await client.DeleteAsync($"/api/transactions/{created.Id}");

        var active = await client.GetFromJsonAsync<PagedResult<TransactionDto>>(
            "/api/transactions?status=Active", Json);
        var inactive = await client.GetFromJsonAsync<PagedResult<TransactionDto>>(
            "/api/transactions?status=Inactive", Json);

        active!.TotalCount.ShouldBe(0);
        inactive!.TotalCount.ShouldBe(1);
    }

    [SkippableFact]
    public async Task Delete_of_an_id_that_never_existed_is_404()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(TestTokens.Manager);

        var response = await client.DeleteAsync($"/api/transactions/{Guid.CreateVersion7()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, Guid id, string etag, string type)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/transactions/{id}")
        {
            Content = JsonContent.Create(new
            {
                transactionDate = "2026-08-21T10:00:00Z",
                transactionType = type,
            }),
        };

        request.Headers.TryAddWithoutValidation("If-Match", etag);

        return client.SendAsync(request);
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

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<TransactionDto>(Json))!;
    }
}
