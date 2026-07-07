using FiscalFox.Api.Services;
using FiscalFox.Domain.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace FiscalFox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analytics;

    public AnalyticsController(AnalyticsService analytics) => _analytics = analytics;

    /// <summary>
    /// Full portfolio analytics (value, weights, risk, rebalancing plan) for an account.
    /// The math is executed by the F# analytics engine.
    /// </summary>
    [HttpGet("portfolio/{accountId:int}")]
    public async Task<ActionResult<PortfolioReport>> Portfolio(
        int accountId,
        [FromQuery] double riskFree = 0.04,
        [FromQuery] double minTrade = 50.0,
        CancellationToken ct = default)
    {
        var report = await _analytics.BuildReportAsync(accountId, riskFree, minTrade, ct);
        if (report is null)
            return NotFound($"Account {accountId} not found.");
        return Ok(report);
    }

    /// <summary>Risk/return stats for one instrument over its cached price history.</summary>
    [HttpGet("instrument/{symbol}")]
    public async Task<ActionResult<RiskReturnStats>> Instrument(
        string symbol,
        [FromQuery] double riskFree = 0.04,
        CancellationToken ct = default)
    {
        var stats = await _analytics.InstrumentRiskAsync(symbol, riskFree, ct);
        if (stats is null)
            return NotFound($"Instrument '{symbol}' not found.");
        return Ok(stats);
    }

    /// <summary>
    /// The account's value-weighted equity curve + running drawdown, ready to
    /// plot. Surfaces the F# engine's cumulative wealth index.
    /// </summary>
    [HttpGet("portfolio/{accountId:int}/timeseries")]
    public async Task<ActionResult<PortfolioTimeseries>> Timeseries(
        int accountId,
        CancellationToken ct = default)
    {
        var series = await _analytics.BuildTimeseriesAsync(accountId, ct);
        if (series is null)
            return NotFound($"Account {accountId} not found.");
        return Ok(series);
    }

    /// <summary>
    /// Pearson correlation matrix across instruments. Pass <c>?symbols=VTI,BND</c>
    /// to restrict it; omit to correlate every instrument with price history.
    /// </summary>
    [HttpGet("correlation")]
    public async Task<ActionResult<CorrelationMatrix>> Correlation(
        [FromQuery] string? symbols = null,
        CancellationToken ct = default)
    {
        var list = string.IsNullOrWhiteSpace(symbols)
            ? null
            : symbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matrix = await _analytics.CorrelationAsync(list, ct);
        return Ok(matrix);
    }

    /// <summary>
    /// Markowitz efficient frontier for an account: a cloud of random long-only
    /// portfolios over its holdings, the max-Sharpe portfolio, and the current
    /// allocation — all on the annualized risk/return plane.
    /// </summary>
    [HttpGet("frontier/{accountId:int}")]
    public async Task<ActionResult<EfficientFrontier>> Frontier(
        int accountId,
        [FromQuery] double riskFree = 0.04,
        [FromQuery] int samples = 3000,
        [FromQuery] int seed = 12345,
        CancellationToken ct = default)
    {
        var frontier = await _analytics.FrontierAsync(accountId, riskFree, samples, seed, ct);
        if (frontier is null)
            return NotFound($"Account {accountId} not found.");
        return Ok(frontier);
    }
}
