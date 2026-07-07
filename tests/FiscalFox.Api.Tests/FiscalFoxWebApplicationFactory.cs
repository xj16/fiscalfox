using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FiscalFox.Api.Tests;

/// <summary>
/// Boots the real API in-process with the EF Core in-memory provider and the
/// repository's cached price CSVs, so tests exercise the full stack (controllers,
/// EF, and the F# analytics engine) without needing a MySQL server.
/// </summary>
public class FiscalFoxWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var priceDir = LocatePriceDirectory();

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FiscalFox:DatabaseProvider"] = "InMemory",
                ["FiscalFox:PriceDirectory"] = priceDir,
                // Keep the whole suite well under any window so the shared-host
                // request volume never trips the rate limiter.
                ["FiscalFox:RateLimit:PermitPerWindow"] = "100000"
            });
        });
    }

    /// <summary>Walk up from the test output dir to find the repo's data/prices folder.</summary>
    private static string LocatePriceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "prices");
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.csv").Any())
                return candidate;
            dir = dir.Parent;
        }

        // Fall back to the conventional default; seeding will simply find no files.
        return Path.Combine(AppContext.BaseDirectory, "data", "prices");
    }
}
