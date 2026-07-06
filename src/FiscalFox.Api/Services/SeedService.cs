using FiscalFox.Api.Data;
using FiscalFox.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FiscalFox.Api.Services;

/// <summary>
/// Ensures the database schema exists and is populated with instruments,
/// cached prices, and a sample demo portfolio on first run. Idempotent.
/// </summary>
public class SeedService
{
    private static readonly Dictionary<string, (string Name, AssetClass Class)> Meta = new()
    {
        ["VTI"] = ("Vanguard Total US Stock Market ETF", AssetClass.Equity),
        ["VXUS"] = ("Vanguard Total International Stock ETF", AssetClass.Equity),
        ["BND"] = ("Vanguard Total Bond Market ETF", AssetClass.Bond),
        ["GLD"] = ("SPDR Gold Shares", AssetClass.Commodity),
        ["BTC-USD"] = ("Bitcoin", AssetClass.Crypto),
    };

    private readonly FiscalFoxDbContext _db;
    private readonly ILogger<SeedService> _log;
    private readonly string _priceDir;

    public SeedService(FiscalFoxDbContext db, ILogger<SeedService> log, IConfiguration config)
    {
        _db = db;
        _log = log;
        _priceDir = config["FiscalFox:PriceDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "data", "prices");
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await ImportInstrumentsAndPricesAsync(ct);
        await EnsureDemoPortfolioAsync(ct);
    }

    private async Task ImportInstrumentsAndPricesAsync(CancellationToken ct)
    {
        foreach (var (symbol, path) in PriceCsvLoader.Discover(_priceDir))
        {
            var instrument = await _db.Instruments
                .FirstOrDefaultAsync(i => i.Symbol == symbol, ct);

            if (instrument is null)
            {
                var (name, cls) = Meta.TryGetValue(symbol, out var m) ? m : (symbol, AssetClass.Equity);
                instrument = new Instrument
                {
                    Symbol = symbol,
                    Name = name,
                    AssetClass = cls,
                    Currency = "USD"
                };
                _db.Instruments.Add(instrument);
                await _db.SaveChangesAsync(ct);
            }

            var hasPrices = await _db.PriceBars.AnyAsync(p => p.InstrumentId == instrument.Id, ct);
            if (hasPrices)
                continue;

            var bars = PriceCsvLoader.ParseFile(path);
            foreach (var bar in bars)
                bar.InstrumentId = instrument.Id;

            _db.PriceBars.AddRange(bars);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Imported {Count} price bars for {Symbol}", bars.Count, symbol);
        }
    }

    private async Task EnsureDemoPortfolioAsync(CancellationToken ct)
    {
        if (await _db.Accounts.AnyAsync(ct))
            return;

        var account = new Account
        {
            Name = "Demo Portfolio",
            Currency = "USD",
            Type = AccountType.Brokerage,
            CashBalance = 2_500m
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(ct);

        // A simple diversified 60/20/10/10-ish target allocation.
        var demo = new (string Symbol, decimal Qty, decimal Target)[]
        {
            ("VTI", 40m, 0.50m),
            ("VXUS", 30m, 0.20m),
            ("BND", 50m, 0.20m),
            ("GLD", 10m, 0.10m),
        };

        foreach (var (symbol, qty, target) in demo)
        {
            var instrument = await _db.Instruments.FirstOrDefaultAsync(i => i.Symbol == symbol, ct);
            if (instrument is null)
                continue;

            var lastClose = await _db.PriceBars
                .Where(p => p.InstrumentId == instrument.Id)
                .OrderByDescending(p => p.Date)
                .Select(p => p.Close)
                .FirstOrDefaultAsync(ct);

            _db.Holdings.Add(new Holding
            {
                AccountId = account.Id,
                InstrumentId = instrument.Id,
                Quantity = qty,
                AverageCost = lastClose == 0 ? 100m : lastClose * 0.9m,
                TargetWeight = target
            });
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Seeded demo portfolio with {Count} holdings", demo.Length);
    }
}
