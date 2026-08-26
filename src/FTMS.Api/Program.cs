// design: doc 03 section 1 - FTMS.Api is the composition root and nothing else.
// Fleshed out in phase 5; this keeps the skeleton building from phase 1 onward.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

await app.RunAsync();

/// <summary>Exposed so WebApplicationFactory can boot the real pipeline. design: doc 08 section 3.</summary>
public partial class Program;
