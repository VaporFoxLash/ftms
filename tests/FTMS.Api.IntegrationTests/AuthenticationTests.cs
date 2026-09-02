using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FTMS.Api.IntegrationTests.Infrastructure;

namespace FTMS.Api.IntegrationTests;

/// <summary>
/// The login, refresh and logout cycle against real PBKDF2 hashes and a real RefreshTokens table.
///
/// design: doc 06 section 3 and doc 08 section 3. These tests sign in through the actual endpoint
/// rather than minting a token, because a login that is never exercised end to end is a login
/// nobody has proved works. TestTokens still mints directly for the authorization matrix, where
/// bypassing the credential check is the point.
/// </summary>
[Collection(ApiCollection.Name)]
public class AuthenticationTests(FtmsApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string CookieName = "ftms_rt";

    public Task InitializeAsync() =>
        DockerAvailability.IsAvailable ? factory.ResetDatabaseAsync() : Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Valid_credentials_return_an_access_token_and_a_session_cookie()
    {
        DockerAvailability.RequireDocker();
        var client = factory.CreateClient();

        var response = await SignIn(client, TestUsers.Manager, TestUsers.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var session = await response.Content.ReadFromJsonAsync<SessionBody>(Json);
        session!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        session.UserName.ShouldBe(TestUsers.Manager);
        session.Roles.ShouldContain("Manager");
        session.ExpiresInSeconds.ShouldBeGreaterThan(0);

        // The refresh token must never appear in the body. If it does, an XSS can read it, and
        // the httpOnly cookie was pointless.
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("refreshToken");
    }

    [SkippableFact]
    public async Task The_session_cookie_is_httponly_and_samesite_strict()
    {
        DockerAvailability.RequireDocker();
        var client = factory.CreateClient();

        var response = await SignIn(client, TestUsers.Manager, TestUsers.Password);
        var cookie = SetCookieHeader(response);

        cookie.ShouldNotBeNull("login must set the refresh cookie.");

        // HttpOnly is what keeps script away from a credential that renews sessions for a
        // fortnight. SameSite=Strict is the CSRF control, and it is why there is no double
        // submit token: the cookie is simply never attached to a cross site request.
        cookie.ToLowerInvariant().ShouldContain("httponly");
        cookie.ToLowerInvariant().ShouldContain("samesite=strict");
        cookie.ToLowerInvariant().ShouldContain("path=/api/auth");
    }

    [SkippableFact]
    public async Task An_unknown_user_and_a_wrong_password_are_indistinguishable()
    {
        DockerAvailability.RequireDocker();
        var client = factory.CreateClient();

        var unknown = await SignIn(client, "nobody.at.all", TestUsers.Password);
        var wrong = await SignIn(client, TestUsers.Auditor, "WrongPassword#2026");

        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Identical bodies, or the endpoint becomes an account enumeration oracle: an attacker
        // could sort a username list into real and fake without guessing a single password.
        var unknownProblem = await unknown.Content.ReadAsStringAsync();
        var wrongProblem = await wrong.Content.ReadAsStringAsync();

        Detail(wrongProblem).ShouldBe(Detail(unknownProblem));
        ProblemType(wrongProblem).ShouldBe(ProblemType(unknownProblem));
    }

    [SkippableFact]
    public async Task Repeated_failures_lock_the_account()
    {
        DockerAvailability.RequireDocker();
        var client = factory.CreateClient();

        // AddFtmsIdentity sets MaxFailedAccessAttempts to 5.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await SignIn(client, TestUsers.Lockout, "WrongPassword#2026");
            failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // 423 rather than 401, and note it now says so even for the CORRECT password. That leaks
        // nothing: you only reach this state by already knowing the account exists.
        var locked = await SignIn(client, TestUsers.Lockout, TestUsers.Password);
        locked.StatusCode.ShouldBe(HttpStatusCode.Locked);
    }

    [SkippableFact]
    public async Task The_access_token_from_login_actually_opens_the_api()
    {
        DockerAvailability.RequireDocker();
        var client = factory.CreateClient();

        var session = await SignInAndRead(client, TestUsers.Auditor);

        var listed = await BearerClient(session).GetAsync("/api/transactions");

        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Refresh_rotates_the_cookie_and_issues_a_new_access_token()
    {
        DockerAvailability.RequireDocker();
        var client = factory.CreateClient();

        var login = await SignIn(client, TestUsers.Manager, TestUsers.Password);
        var firstCookie = CookieValue(login);

        var refreshed = await Refresh(client, firstCookie);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var secondCookie = CookieValue(refreshed);
        secondCookie.ShouldNotBe(firstCookie, "refresh tokens are single use and must rotate.");

        var session = await refreshed.Content.ReadFromJsonAsync<SessionBody>(Json);
        session!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        session.UserName.ShouldBe(TestUsers.Manager);
    }

    [SkippableFact]
    public async Task Replaying_a_spent_refresh_token_kills_the_whole_chain()
    {
        // The security property this design exists for. A refresh token presented twice means two
        // parties hold it and one of them is not the user. We cannot tell which, so both sessions
        // end: the real user signs in again, the attacker gets nothing.
        DockerAvailability.RequireDocker();
        var client = factory.CreateClient();

        var login = await SignIn(client, TestUsers.Manager, TestUsers.Password);
        var stolen = CookieValue(login);

        var legitimate = await Refresh(client, stolen);
        legitimate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var successor = CookieValue(legitimate);

        // The attacker replays the spent token.
        var replay = await Refresh(client, stolen);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the successor held by the legitimate user is now dead too. That is the point: the
        // chain is revoked, not merely the replayed link.
        var afterReplay = await Refresh(client, successor);
        afterReplay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Refresh_without_a_cookie_is_401()
    {
        DockerAvailability.RequireDocker();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/auth/refresh", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Logout_revokes_the_refresh_token()
    {
        DockerAvailability.RequireDocker();
        var client = factory.CreateClient();

        var login = await SignIn(client, TestUsers.Manager, TestUsers.Password);
        var cookie = CookieValue(login);
        var session = (await login.Content.ReadFromJsonAsync<SessionBody>(Json))!;

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        logout.Headers.Add("Cookie", CookieName + "=" + cookie);

        var loggedOut = await client.SendAsync(logout);
        loggedOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The access token stays valid for its remaining minutes - it cannot be revoked, which is
        // exactly why it is short lived. What logout guarantees is that the session cannot be
        // RENEWED past that.
        var reuse = await Refresh(client, cookie);
        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Me_reports_the_caller_and_their_roles()
    {
        DockerAvailability.RequireDocker();
        var client = factory.CreateClient();

        var session = await SignInAndRead(client, TestUsers.Capturer);

        var me = await BearerClient(session).GetFromJsonAsync<CurrentUserBody>("/api/auth/me", Json);

        me!.UserName.ShouldBe(TestUsers.Capturer);
        me.Roles.ShouldBe(["Capturer"]);
    }

    [SkippableFact]
    public async Task Me_is_refused_without_a_token()
    {
        DockerAvailability.RequireDocker();

        var response = await factory.CreateClient().GetAsync("/api/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task The_development_token_endpoint_is_gone()
    {
        // It minted a Manager token for any username, verifying nothing. Its removal is the most
        // important part of this change, so it gets a test rather than a comment - and the host
        // runs in Development here, which is the only environment it was ever mapped in.
        DockerAvailability.RequireDocker();

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/dev/token",
            new { userName = "anyone", roles = new[] { "Manager" } });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task The_audit_trail_records_the_signed_in_user_not_a_placeholder()
    {
        DockerAvailability.RequireDocker();
        var client = factory.CreateClient();

        var session = await SignInAndRead(client, TestUsers.Capturer);

        var created = await BearerClient(session).PostAsJsonAsync("/api/transactions", new
        {
            transactionDate = "2026-08-20T09:30:00Z",
            transactionType = "Deposit",
            amount = 42.00m,
            currencyCode = "ZAR",
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        // ICurrentUser is registered once, by the API layer, and reads the name claim. Proving it
        // end to end is what stops the audit trail quietly regressing to the "system" placeholder
        // it used to stamp on every row.
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(factory.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP 1 ChangedBy FROM TransactionAudits ORDER BY ChangedAtUtc DESC";

        var changedBy = (string?)await command.ExecuteScalarAsync();

        changedBy.ShouldBe(TestUsers.Capturer);
    }

    private Task<HttpResponseMessage> SignIn(HttpClient client, string userName, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { userName, password });

    private async Task<SessionBody> SignInAndRead(HttpClient client, string userName)
    {
        var response = await SignIn(client, userName, TestUsers.Password);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<SessionBody>(Json))!;
    }

    private HttpClient BearerClient(SessionBody session)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        return client;
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string cookieValue)
    {
        // These clients keep no cookie jar, so the cookie is attached by hand. That is closer to
        // the truth anyway: it makes explicit which token each call presents, which is the entire
        // subject of the replay test above.
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", CookieName + "=" + cookieValue);

        return client.SendAsync(request);
    }

    private static string? SetCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value => value.StartsWith(CookieName, StringComparison.Ordinal))
            : null;

    private static string CookieValue(HttpResponseMessage response)
    {
        var header = SetCookieHeader(response);
        header.ShouldNotBeNull("expected a refresh cookie on this response.");

        return header.Split(';')[0][(CookieName.Length + 1)..];
    }

    private static string? Detail(string problemJson) => Read(problemJson, "detail");

    private static string? ProblemType(string problemJson) => Read(problemJson, "type");

    private static string? Read(string problemJson, string property) =>
        JsonDocument.Parse(problemJson).RootElement.TryGetProperty(property, out var value)
            ? value.GetString()
            : null;

    private sealed record SessionBody(
        string AccessToken,
        int ExpiresInSeconds,
        string UserName,
        string DisplayName,
        string[] Roles);

    private sealed record CurrentUserBody(string UserName, string DisplayName, string[] Roles);
}
