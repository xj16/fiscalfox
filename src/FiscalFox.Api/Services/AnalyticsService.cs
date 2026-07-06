using FiscalFox.Analytics;
using FiscalFox.Api.Data;
using FiscalFox.Domain.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace FiscalFox.Api.Services;

/// <summary>
/// Bridges EF Core entities into the F# analytics engine and returns the
/// computed <see cref="PortfolioReport"/>. All heavy math lives in F#.
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
        var account = await _db.Accounts
            .Include(a => a.Holdings)
            .ThenInclude(h => h.Instrument)
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);

        if (account is null)
            return null;

        var holdingInputs = new List<Portfolio.HoldingInput>();

        foreach (var holding in account.Holdings)
        {
            if (holding.Instrument is null)
                continue;

            var closes = await _db.PriceBars
                .Where(p => p.InstrumentId == holding.InstrumentId)
                .OrderBy(p => p.Date)
                .Select(p => (double)p.Close)
                .ToListAsync(ct);

            var target = holding.TargetWeight.HasValue
                ? FSharpOption<double>.Some((double)holding.TargetWeight.Value)
                : FSharpOption<double>.None;

            holdingInputs.Add(new Portfolio.HoldingInput(
                holding.Instrument.Symbol,
                (double)holding.Quantity,
                ListModule.OfSeq(closes),
                target));
        }

        return Portfolio.report(annualRiskFree, minTrade, ListModule.OfSeq(holdingInputs));
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
}
