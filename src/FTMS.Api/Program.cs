using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FTMS.Api.Authentication;
using FTMS.Api.Middleware;
using FTMS.Api.Serialization;
using FTMS.Application;
using FTMS.Infrastructure;
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
    options.AddSchemaTransformer<DecimalSchemaTransformer>());

builder.Services.AddProblemDetails();

// design: doc 06 section 4 - CORS locked to the exact Angular origin with credentials allowed
// and nothing wildcarded.
const string SpaCorsPolicy = "ftms-spa";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200"];

builder.Services.AddCors(options => options.AddPolicy(SpaCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()

    // The SPA reads the ETag to send back as If-Match, and a browser hides response headers
    // from script unless they are explicitly exposed. design: doc 05 section 6.
    .WithExposedHeaders("ETag", "Location")));

// design: doc 06 section 4 - a global sliding window. doc 07 section 4 notes this doubles as a
// performance control, keeping one misbehaving client from eating the four cores Express gets.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

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

    // TODO design: doc 06 section 4 - add a strict bucket on the login endpoint to blunt
    // credential stuffing, once that endpoint exists.
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

    app.MapDevelopmentTokenEndpoint();

    // design: doc 07 - migrations apply on startup in Development only. In any other
    // environment they run at deployment time under a separate elevated login, because the
    // application's own login has no DDL rights (doc 06 section 5.1).
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FtmsDbContext>().Database.MigrateAsync();
}
else
{
    // design: doc 06 section 4 - HTTPS redirection and HSTS on, TLS 1.2 minimum.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(SpaCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// No RequireRateLimiting here on purpose: the GlobalLimiter configured above already applies
// to every request that passes through UseRateLimiter. Naming a policy per endpoint would mean
// registering one, and an endpoint asking for a policy that does not exist throws at request
// time rather than at startup.
app.MapControllers();

// design: doc 06 section 3 - no anonymous endpoints except login and health.
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
