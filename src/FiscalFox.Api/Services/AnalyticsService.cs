using FiscalFox.Analytics;
using FiscalFox.Api.Data;
using FiscalFox.Domain.Analytics;
using FiscalFox.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace FiscalFox.Api.Services;

/// <summary>
/// Bridges EF Core entities into the F# analytics engine and returns the
/// computed analytics DTOs. All heavy math lives in F#; this class is only glue
/// (EF query → F# input shape → F# call → DTO).
/// </summary>
public class AnalyticsService
{
    private readonly FiscalFoxDbContext _db;

    public AnalyticsService(FiscalFoxDbContext db) => _db = db;

    /// <summary>
    /// Build a full analytics report for an account.
    /// </summary>
    /// <param name="annualRiskFree">Annual risk-free rate for Sharpe (e.g. 0.04).</param>
    /// <param name="minTrade">Suppress rebalance trades below this cash amount.</param>
    public async Task<PortfolioReport?> BuildReportAsync(
        int accountId,
        double annualRiskFree = 0.04,
        double minTrade = 50.0,
        CancellationToken ct = default)
    {
        var inputs = await LoadHoldingInputsAsync(accountId, ct);
        if (inputs is null)
            return null;

        return Portfolio.report(annualRiskFree, minTrade, ListModule.OfSeq(inputs));
    }

    /// <summary>
    /// The value-weighted equity curve + drawdown series for an account,
    /// ready to plot. Exposes the F# engine's <c>cumulativeIndex</c> output.
    /// </summary>
    public async Task<PortfolioTimeseries?> BuildTimeseriesAsync(
        int accountId,
        CancellationToken ct = default)
    {
        var inputs = await LoadHoldingInputsAsync(accountId, ct);
        if (inputs is null)
            return null;

        return Portfolio.timeseries(ListModule.OfSeq(inputs));
    }

    /// <summary>Per-instrument risk stats over its cached price history.</summary>
    public async Task<RiskReturnStats?> InstrumentRiskAsync(
        string symbol,
        double annualRiskFree = 0.04,
        CancellationToken ct = default)
    {
        var instrument = await _db.Instruments
            .FirstOrDefaultAsync(i => i.Symbol == symbol, ct);
        if (instrument is null)
            return null;

        var closes = await _db.PriceBars
            .Where(p => p.InstrumentId == instrument.Id)
            .OrderBy(p => p.Date)
            .Select(p => (double)p.Close)
            .ToListAsync(ct);

        return Risk.statsFromPrices(annualRiskFree, ListModule.OfSeq(closes));
    }

    /// <summary>
    /// Pearson correlation matrix across a set of symbols (defaults to every
    /// instrument that has price history). Computed by the F# engine over
    /// tail-aligned daily returns.
    /// </summary>
    public async Task<CorrelationMatrix> CorrelationAsync(
        IReadOnlyCollection<string>? symbols,
        CancellationToken ct = default)
    {
        var query = _db.Instruments.AsQueryable();
        if (symbols is { Count: > 0 })
        {
            var set = symbols.Select(s => s.ToUpperInvariant()).ToHashSet();
            query = query.Where(i => set.Contains(i.Symbol.ToUpper()));
        }

        var instruments = await query.OrderBy(i => i.Symbol).ToListAsync(ct);
        var closesById = await LoadClosesAsync(instruments.Select(i => i.Id).ToList(), ct);

        var assets = instruments
            .Select(i => new PortfolioMath.AssetSeries(
                i.Symbol,
                ListModule.OfSeq(closesById.TryGetValue(i.Id, out var c) ? c : new List<double>())))
            .Where(a => !ListModule.IsEmpty(a.Prices))
            .ToList();

        return PortfolioMath.correlationMatrix(ListModule.OfSeq(assets));
    }

    /// <summary>
    /// Markowitz efficient frontier for an account: many random long-only
    /// portfolios over the account's holdings, the max-Sharpe portfolio, and the
    /// account's current allocation plotted on the same risk/return axes.
    /// </summary>
    public async Task<EfficientFrontier?> FrontierAsync(
        int accountId,
        double annualRiskFree = 0.04,
        int samples = 3000,
        int seed = 12345,
        CancellationToken ct = default)
    {
        var account = await _db.Accounts
            .Include(a => a.Holdings)
            .ThenInclude(h => h.Instrument)
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
            return null;

        var holdings = account.Holdings.Where(h => h.Instrument is not null).ToList();
        var closesById = await LoadClosesAsync(holdings.Select(h => h.InstrumentId).ToList(), ct);

        var assets = new List<PortfolioMath.AssetSeries>();
        var marketValues = new List<double>();
        foreach (var h in holdings)
        {
            var closes = closesById.TryGetValue(h.InstrumentId, out var c) ? c : new List<double>();
            if (closes.Count < 2)
                continue;
            assets.Add(new PortfolioMath.AssetSeries(h.Instrument!.Symbol, ListModule.OfSeq(closes)));
            marketValues.Add((double)h.Quantity * closes[^1]);
        }

        // Current weights from live market values (skip when everything is zero).
        var totalValue = marketValues.Sum();
        FSharpOption<FSharpList<double>> current = FSharpOption<FSharpList<double>>.None;
        if (totalValue > 0)
        {
            var weights = marketValues.Select(mv => mv / totalValue).ToList();
            current = FSharpOption<FSharpList<double>>.Some(ListModule.OfSeq(weights));
        }

        samples = Math.Clamp(samples, 100, 20000);

        return PortfolioMath.efficientFrontier(
            annualRiskFree, samples, seed, current, ListModule.OfSeq(assets));
    }

    // -----------------------------------------------------------------------
    // Shared loaders (single grouped query — no per-holding N+1).
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build the F# <c>HoldingInput</c> list for an account, or null if the
    /// account does not exist. Loads all needed closes in one query.
    /// </summary>
    private async Task<List<Portfolio.HoldingInput>?> LoadHoldingInputsAsync(
        int accountId,
        CancellationToken ct)
    {
        var account = await _db.Accounts
            .Include(a => a.Holdings)
            .ThenInclude(h => h.Instrument)
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);

        if (account is null)
            return null;

        var holdings = account.Holdings.Where(h => h.Instrument is not null).ToList();
        var closesById = await LoadClosesAsync(holdings.Select(h => h.InstrumentId).ToList(), ct);

        var inputs = new List<Portfolio.HoldingInput>();
        foreach (var holding in holdings)
        {
            var closes = closesById.TryGetValue(holding.InstrumentId, out var c) ? c : new List<double>();

            var target = holding.TargetWeight.HasValue
                ? FSharpOption<double>.Some((double)holding.TargetWeight.Value)
                : FSharpOption<double>.None;

            inputs.Add(new Portfolio.HoldingInput(
                holding.Instrument!.Symbol,
                (double)holding.Quantity,
                ListModule.OfSeq(closes),
                target));
        }

        return inputs;
    }

    /// <summary>
    /// Load ascending closing prices for a set of instruments in a single query,
    /// grouped by instrument id. Replaces the previous per-holding "last close"
    /// / per-instrument queries that caused N+1 round-trips.
    /// </summary>
    private async Task<Dictionary<int, List<double>>> LoadClosesAsync(
        IReadOnlyCollection<int> instrumentIds,
        CancellationToken ct)
    {
        if (instrumentIds.Count == 0)
            return new Dictionary<int, List<double>>();

        var idSet = instrumentIds.ToHashSet();

        var rows = await _db.PriceBars
            .Where(p => idSet.Contains(p.InstrumentId))
            .OrderBy(p => p.InstrumentId).ThenBy(p => p.Date)
            .Select(p => new { p.InstrumentId, Close = (double)p.Close })
            .ToListAsync(ct);

        var result = new Dictionary<int, List<double>>();
        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.InstrumentId, out var list))
            {
                list = new List<double>();
                result[row.InstrumentId] = list;
            }
            list.Add(row.Close);
        }
        return result;
    }
}
