using System.Net;
using System.Net.Http.Json;
using FTMS.Api.IntegrationTests.Infrastructure;

namespace FTMS.Api.IntegrationTests;

/// <summary>
/// Every endpoint called as every role plus anonymous, asserting the exact expected status.
/// design: doc 08 section 7.3 - the quarterly internal authorization matrix sweep, encoded as
/// a test so it runs on every commit rather than four times a year.
/// </summary>
[Collection(ApiCollection.Name)]
public class AuthorizationMatrixTests(FtmsApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() =>
        DockerAvailability.IsAvailable ? factory.ResetDatabaseAsync() : Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    // Capturer creates and updates. Manager adds soft delete. Auditor reads everything,
    // including Inactive records. Admin manages users and has NO transaction rights, because
    // separating duty between administering the system and moving money through it is
    // elementary financial control. design: doc 06 section 3 and decision 2.
    [SkippableTheory]
    [InlineData(TestTokens.Capturer, HttpStatusCode.OK)]
    [InlineData(TestTokens.Manager, HttpStatusCode.OK)]
    [InlineData(TestTokens.Auditor, HttpStatusCode.OK)]
    [InlineData(TestTokens.Admin, HttpStatusCode.Forbidden)]
    public async Task Reading_the_list(string role, HttpStatusCode expected)
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(role);

        (await client.GetAsync("/api/transactions")).StatusCode.ShouldBe(expected);
    }

    [SkippableTheory]
    [InlineData(TestTokens.Capturer, HttpStatusCode.OK)]
    [InlineData(TestTokens.Manager, HttpStatusCode.OK)]
    [InlineData(TestTokens.Auditor, HttpStatusCode.OK)]
    [InlineData(TestTokens.Admin, HttpStatusCode.Forbidden)]
    public async Task Reading_the_statuses(string role, HttpStatusCode expected)
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(role);

        (await client.GetAsync("/api/transactionstatuses")).StatusCode.ShouldBe(expected);
    }

    [SkippableTheory]
    [InlineData(TestTokens.Capturer, HttpStatusCode.Created)]
    [InlineData(TestTokens.Manager, HttpStatusCode.Created)]
    [InlineData(TestTokens.Auditor, HttpStatusCode.Forbidden)]
    [InlineData(TestTokens.Admin, HttpStatusCode.Forbidden)]
    public async Task Creating_a_transaction(string role, HttpStatusCode expected)
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs(role);

        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            transactionDate = "2026-08-20T09:30:00Z",
            transactionType = "Deposit",
            amount = 100m,
        });

        response.StatusCode.ShouldBe(expected);
    }

    [SkippableTheory]
    [InlineData(TestTokens.Capturer, HttpStatusCode.Forbidden)]
    [InlineData(TestTokens.Manager, HttpStatusCode.NoContent)]
    [InlineData(TestTokens.Auditor, HttpStatusCode.Forbidden)]
    [InlineData(TestTokens.Admin, HttpStatusCode.Forbidden)]
    public async Task Soft_deleting_a_transaction(string role, HttpStatusCode expected)
    {
        // Capturer creating and updating but NOT deleting is the whole point of having two
        // money touching roles. design: doc 06 section 3.
        DockerAvailability.RequireDocker();

        var creator = factory.AuthenticatedAs(TestTokens.Manager);
        var created = await creator.PostAsJsonAsync("/api/transactions", new
        {
            transactionDate = "2026-08-20T09:30:00Z",
            transactionType = "Deposit",
            amount = 100m,
        });
        var id = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>())!
            .RootElement.GetProperty("id").GetGuid();

        var client = factory.AuthenticatedAs(role);

        (await client.DeleteAsync($"/api/transactions/{id}")).StatusCode.ShouldBe(expected);
    }

    [SkippableTheory]
    [InlineData("/api/transactions")]
    [InlineData("/api/transactionstatuses")]
    public async Task Anonymous_access_is_refused_everywhere_that_is_not_health(string path)
    {
        DockerAvailability.RequireDocker();

        (await factory.CreateClient().GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task A_tampered_token_signature_is_rejected()
    {
        // design: doc 08 section 7.3 - token abuse cases: expired, tampered signature, wrong
        // audience, replayed one time refresh token.
        DockerAvailability.RequireDocker();

        var token = TestTokens.IssueFor(TestTokens.Manager);
        var tampered = token[..^4] + "AAAA";

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tampered);

        (await client.GetAsync("/api/transactions")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task A_token_with_no_recognised_role_gets_nothing()
    {
        DockerAvailability.RequireDocker();
        var client = factory.AuthenticatedAs("Intern");

        (await client.GetAsync("/api/transactions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
