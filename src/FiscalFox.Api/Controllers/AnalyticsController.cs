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
}
