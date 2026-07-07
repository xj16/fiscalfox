using System.Threading.RateLimiting;
using FiscalFox.Api.Data;
using FiscalFox.Api.Security;
using FiscalFox.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Database provider selection.
//   "MySql"    -> Pomelo/MySQL (production / docker-compose). Default.
//   "InMemory" -> EF Core in-memory store (CI smoke test, quick local demo).
// ---------------------------------------------------------------------------
var provider = builder.Configuration["FiscalFox:DatabaseProvider"] ?? "MySql";

builder.Services.AddDbContext<FiscalFoxDbContext>(options =>
{
    if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
    {
        options.UseInMemoryDatabase("fiscalfox");
    }
    else
    {
        var cs = builder.Configuration.GetConnectionString("FiscalFox")
            ?? "server=localhost;port=3306;database=fiscalfox;user=fiscalfox;password=fiscalfox";
        options.UseMySql(cs, ServerVersion.AutoDetect(cs));
    }
});

builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<SeedService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---------------------------------------------------------------------------
// CORS. Locked to the configured Web origins so an exposed API does not accept
// browser calls from arbitrary sites. Set FiscalFox:AllowedOrigins (comma- or
// array-separated) to override; defaults cover the local Blazor UI. Setting it
// to "*" restores the permissive any-origin behavior for pure-localhost use.
// ---------------------------------------------------------------------------
const string CorsPolicy = "fiscalfox-web";
var allowedOrigins = (builder.Configuration["FiscalFox:AllowedOrigins"]
        ?? "http://localhost:5090;https://localhost:5090")
    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        if (allowedOrigins.Contains("*"))
            policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true);
        else
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

// ---------------------------------------------------------------------------
// Rate limiting. A fixed-window limiter caps requests per client so a single
// caller cannot hammer the analytics endpoints. Tunable via FiscalFox:RateLimit.
// ---------------------------------------------------------------------------
var permitPerWindow = builder.Configuration.GetValue("FiscalFox:RateLimit:PermitPerWindow", 300);
var windowSeconds = builder.Configuration.GetValue("FiscalFox:RateLimit:WindowSeconds", 60);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitPerWindow,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

// Apply schema + seed data. Resilient: a missing DB should not crash startup
// so `swagger` and `/api/health` still work while the operator fixes MySQL.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<FiscalFoxDbContext>();
        // EnsureCreated builds the schema directly from the EF model, so the app
        // works out of the box without shipping migration files.
        await db.Database.EnsureCreatedAsync();

        var seeder = services.GetRequiredService<SeedService>();
        await seeder.SeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database init/seed failed. API will start but data endpoints may error until the DB is reachable.");
    }
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors(CorsPolicy);
app.UseRateLimiter();
// Optional API key. No-op unless FiscalFox:ApiKey is configured, so the
// zero-config demo and CI stay open while a self-hoster can lock it down.
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

app.Run();

// Expose the implicit Program class to the test host (WebApplicationFactory).
public partial class Program { }
