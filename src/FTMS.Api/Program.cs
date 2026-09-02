using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FTMS.Api.Authentication;
using FTMS.Api.Middleware;
using FTMS.Api.Serialization;
using FTMS.Application;
using FTMS.Infrastructure;
using FTMS.Infrastructure.Identity;
using FTMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

// design: doc 03 section 1 - FTMS.Api is the composition root and nothing else. Every rule,
// validation and authorization check lives behind this file, in the layers it wires together.
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddFtmsAuthentication(builder.Configuration, builder.Environment);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // design: doc 05 section 1 - camelCase property names, timestamps in UTC ISO 8601.
        // The trailing Z is contract, not cosmetics: without it a browser reads every
        // timestamp as local time, which is two hours wrong in SAST.
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
        options.JsonSerializerOptions.Converters.Add(new NullableUtcDateTimeJsonConverter());
    });

// design: doc 05 section 9 - OpenAPI is the single client facing contract; both clients
// generate their API layers from it, so neither hand writes DTOs that can drift.
builder.Services.AddOpenApi("v1", options =>
{
    options.AddSchemaTransformer<DecimalSchemaTransformer>();

    // Without these the published contract described every endpoint as anonymous, while the
    // code required a bearer token on all but three. design: doc 05 section 9.
    options.AddDocumentTransformer<SecuritySchemeTransformer>();
    options.AddOperationTransformer<SecurityRequirementTransformer>();
});

// AddProblemDetails() used to be registered here and did nothing at all: it only takes effect
// through UseExceptionHandler or UseStatusCodePages, and this pipeline uses neither.
// ExceptionHandlingMiddleware and ApiControllerBase.Problem between them produce every
// ProblemDetails response the API emits. An inert registration is worse than no registration,
// because it reads like error handling is configured somewhere it is not.

// design: doc 06 section 4 - CORS locked to the exact Angular origin with credentials allowed
// and nothing wildcarded.
const string SpaCorsPolicy = "ftms-spa";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200"];

builder.Services.AddCors(options => options.AddPolicy(SpaCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()

    // Load bearing, not habit: the refresh token is an httpOnly cookie, and a browser will not
    // send a cookie on a cross origin XHR unless the response says credentials are allowed. This
    // is also precisely why WithOrigins names exact origins and why AllowAnyOrigin would be
    // rejected by the browser in combination with this - a wildcard plus credentials is the one
    // CORS combination that is never legal.
    .AllowCredentials()

    // The SPA reads the ETag to send back as If-Match, and a browser hides response headers
    // from script unless they are explicitly exposed. design: doc 05 section 6.
    .WithExposedHeaders("ETag", "Location")));

// design: doc 06 section 4 - a global sliding window. doc 07 section 4 notes this doubles as a
// performance control, keeping one misbehaving client from eating the four cores Express gets.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Partitioning by user name and falling back to IP only works because UseRateLimiter runs
    // AFTER UseAuthentication below. It used to run before, which meant User.Identity was never
    // populated and every request partitioned by IP - so an office behind one NAT shared a
    // single bucket, and an authenticated client got no benefit from being identifiable.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            context.User.Identity?.Name
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            }));

    // design: doc 06 section 4 - the strict bucket on the credential endpoints. Partitioned by
    // IP rather than by user name, deliberately: the attack this blunts is one source trying
    // many accounts, so keying on the account being guessed would give the attacker a fresh
    // allowance per guess. Identity's per account lockout covers the other direction.
    //
    // Configurable rather than hard coded, and that is not ceremony. Ten attempts per five
    // minutes is right for production and wrong for a machine running an end to end suite, where
    // four browsers sign in within seconds of each other and a developer re-runs it repeatedly -
    // the first version of this locked the test suite out of its own application. A threshold
    // that cannot be tuned per environment is one that eventually gets deleted by whoever it
    // inconveniences, which is a far worse outcome than a looser value in Development.
    var authPermitLimit = builder.Configuration.GetValue("RateLimiting:Authentication:PermitLimit", 10);
    var authWindowMinutes = builder.Configuration.GetValue("RateLimiting:Authentication:WindowMinutes", 5);

    options.AddPolicy(RateLimitPolicies.Authentication, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromMinutes(authWindowMinutes),
                QueueLimit = 0,
            }));
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<FtmsDbContext>("database");

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    // design: doc 05 section 9 - Swagger UI in development only.
    app.MapOpenApi("/openapi/{documentName}.json");
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "FTMS v1");
        options.RoutePrefix = "swagger";
    });

    // design: doc 07 - migrations apply on startup in Development only. In any other
    // environment they run at deployment time under a separate elevated login, because the
    // application's own login has no DDL rights (doc 06 section 5.1).
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FtmsDbContext>().Database.MigrateAsync();

    // The four demo accounts, one per role, so the stack is usable the moment it starts and the
    // authorization matrix can be exercised by hand. Refuses to run outside Development - the
    // credentials are in a committed file. design: doc 06 section 3.
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, isDevelopment: true);

    // Sample transactions, but only into an empty table. Without these a freshly cloned
    // repository opens on an empty grid, and paging, sorting and the status filter cannot be
    // judged against zero rows.
    await SampleTransactionSeeder.SeedAsync(scope.ServiceProvider, isDevelopment: true);
}
else
{
    // design: doc 06 section 4 - HTTPS redirection and HSTS on, TLS 1.2 minimum.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(SpaCorsPolicy);

// Authentication BEFORE the rate limiter, which is the reverse of the order this file used to
// have. The global limiter partitions on context.User.Identity?.Name, and that property is only
// populated once the authentication middleware has run - so with the old order the user name was
// unconditionally null and the documented per user partitioning never happened even once.
//
// Anonymous floods are still limited: the partition key falls back to the remote IP, which is
// all that is knowable about an unauthenticated caller anyway.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// No RequireRateLimiting here on purpose: the GlobalLimiter configured above already applies
// to every request that passes through UseRateLimiter. Naming a policy per endpoint would mean
// registering one, and an endpoint asking for a policy that does not exist throws at request
// time rather than at startup.
app.MapControllers();

// design: doc 06 section 3 - no anonymous endpoints except login, refresh and health.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    AllowCachingResponses = false,
}).AllowAnonymous();

await app.RunAsync();

/// <summary>
/// Exposed so WebApplicationFactory can boot the real API pipeline rather than a mock of it.
/// design: doc 08 section 3.
/// </summary>
public partial class Program;
