using FiscalFox.Api.Data;
using FiscalFox.Api.Services;
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
builder.Services.AddScoped<SeedService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

const string CorsPolicy = "fiscalfox-web";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true));
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
app.MapControllers();

app.Run();

// Expose the implicit Program class to the test host (WebApplicationFactory).
public partial class Program { }
