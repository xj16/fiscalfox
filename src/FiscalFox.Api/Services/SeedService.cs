using FiscalFox.Api.Data;
using FiscalFox.Api.Dtos;
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
    private readonly TransactionService _transactions;
    private readonly string _priceDir;

    public SeedService(FiscalFoxDbContext db, ILogger<SeedService> log, TransactionService transactions, IConfiguration config)
    {
        _db = db;
        _log = log;
        _transactions = transactions;
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
            CashBalance = 0m
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(ct);

        // Build the demo portfolio through the real transaction engine so the
        // seeded data has an honest history: a funding deposit, buys that set an
        // actual cost basis, a partial sell that books realized P/L, and a
        // dividend. The resulting holdings land on a diversified target mix.
        var demo = new (string Symbol, decimal Qty, decimal Target)[]
        {
            ("VTI", 40m, 0.50m),
            ("VXUS", 30m, 0.20m),
            ("BND", 50m, 0.20m),
            ("GLD", 10m, 0.10m),
        };

        // Fund the account generously so every buy clears.
        await _transactions.ApplyAsync(account.Id,
            new CreateTransactionDto(TransactionKind.Deposit, null, 0m, 50_000m, 0m, "Initial funding"), ct);

        var seeded = 0;
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
            var buyPrice = lastClose == 0 ? 100m : Math.Round(lastClose * 0.9m, 4); // bought ~10% cheaper

            // Buy a little extra so we can demo a partial sell on the equity sleeve.
            var buyQty = symbol == "VTI" ? qty + 8m : qty;
            await _transactions.ApplyAsync(account.Id,
                new CreateTransactionDto(TransactionKind.Buy, symbol, buyQty, buyPrice, 1m, $"Open {symbol} position"), ct);

            seeded++;
        }

        // Partial sell on VTI to realize a gain, landing on the target quantity.
        var vti = await _db.Instruments.FirstOrDefaultAsync(i => i.Symbol == "VTI", ct);
        if (vti is not null)
        {
            var vtiLast = await _db.PriceBars
                .Where(p => p.InstrumentId == vti.Id)
                .OrderByDescending(p => p.Date).Select(p => p.Close).FirstOrDefaultAsync(ct);
            await _transactions.ApplyAsync(account.Id,
                new CreateTransactionDto(TransactionKind.Sell, "VTI", 8m, Math.Round(vtiLast, 4), 1m, "Trim VTI to target"), ct);

            // A modest dividend on the bond sleeve for a realistic cash line.
            await _transactions.ApplyAsync(account.Id,
                new CreateTransactionDto(TransactionKind.Dividend, "BND", 50m, 0.20m, 0m, "BND quarterly distribution"), ct);
        }

        // Apply the user's target weights (transactions don't set targets).
        foreach (var (symbol, _, target) in demo)
        {
            var holding = await _db.Holdings
                .Include(h => h.Instrument)
                .FirstOrDefaultAsync(h => h.AccountId == account.Id && h.Instrument!.Symbol == symbol, ct);
            if (holding is not null)
                holding.TargetWeight = target;
        }
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Seeded demo portfolio with {Count} holdings via transaction history", seeded);
    }
}
