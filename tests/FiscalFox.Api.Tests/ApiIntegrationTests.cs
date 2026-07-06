using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FiscalFox.Domain.Analytics;
using Xunit;

namespace FiscalFox.Api.Tests;

public class ApiIntegrationTests : IClassFixture<FiscalFoxWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public ApiIntegrationTests(FiscalFoxWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var resp = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Seed_creates_instruments()
    {
        var instruments = await _client.GetFromJsonAsync<List<InstrumentResponse>>("/api/instruments", Json);
        Assert.NotNull(instruments);
        Assert.NotEmpty(instruments!);
        Assert.Contains(instruments!, i => i.Symbol == "VTI");
    }

    [Fact]
    public async Task Seed_creates_demo_account()
    {
        var accounts = await _client.GetFromJsonAsync<List<AccountResponse>>("/api/accounts", Json);
        Assert.NotNull(accounts);
        Assert.NotEmpty(accounts!);
    }

    [Fact]
    public async Task Portfolio_report_is_computed_by_analytics_engine()
    {
        var accounts = await _client.GetFromJsonAsync<List<AccountResponse>>("/api/accounts", Json);
        var accountId = accounts![0].Id;

        var report = await _client.GetFromJsonAsync<PortfolioReport>(
            $"/api/analytics/portfolio/{accountId}", Json);

        Assert.NotNull(report);
        Assert.True(report!.TotalValue > 0);
        Assert.NotEmpty(report.Positions);

        // Weights must sum to ~1.
        var weightSum = report.Positions.Sum(p => p.Weight);
        Assert.InRange(weightSum, 0.999, 1.001);

        // Risk stats should be populated with a sane observation count.
        Assert.True(report.Risk.Observations > 0);
    }

    [Fact]
    public async Task Instrument_risk_endpoint_returns_stats()
    {
        var stats = await _client.GetFromJsonAsync<RiskReturnStats>("/api/analytics/instrument/VTI", Json);
        Assert.NotNull(stats);
        Assert.True(stats!.Observations > 0);
        Assert.True(stats.AnnualizedVolatility >= 0);
    }

    [Fact]
    public async Task Unknown_instrument_risk_returns_404()
    {
        var resp = await _client.GetAsync("/api/analytics/instrument/NOPE");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_account_then_add_holding_round_trips()
    {
        var create = await _client.PostAsJsonAsync("/api/accounts", new
        {
            name = "Test Account",
            currency = "USD",
            type = 0,
            cashBalance = 1000m
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var account = await create.Content.ReadFromJsonAsync<AccountResponse>(Json);
        Assert.NotNull(account);

        var addHolding = await _client.PostAsJsonAsync($"/api/accounts/{account!.Id}/holdings", new
        {
            symbol = "VTI",
            quantity = 5m,
            averageCost = 200m,
            targetWeight = 1.0m
        });
        Assert.Equal(HttpStatusCode.OK, addHolding.StatusCode);

        var holdings = await _client.GetFromJsonAsync<List<HoldingResponse>>(
            $"/api/accounts/{account.Id}/holdings", Json);
        Assert.NotNull(holdings);
        Assert.Contains(holdings!, h => h.Symbol == "VTI" && h.Quantity == 5m);
    }

    [Fact]
    public async Task Add_holding_with_unknown_symbol_is_rejected()
    {
        var accounts = await _client.GetFromJsonAsync<List<AccountResponse>>("/api/accounts", Json);
        var accountId = accounts![0].Id;

        var resp = await _client.PostAsJsonAsync($"/api/accounts/{accountId}/holdings", new
        {
            symbol = "DOES-NOT-EXIST",
            quantity = 1m,
            averageCost = 10m,
            targetWeight = (decimal?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // Local response shapes for deserialization.
    private record AccountResponse(int Id, string Name, string Currency, int Type, decimal CashBalance);
    private record InstrumentResponse(int Id, string Symbol, string Name, int AssetClass, string Currency, decimal? LastClose);
    private record HoldingResponse(int Id, int InstrumentId, string Symbol, decimal Quantity, decimal AverageCost, decimal? TargetWeight, decimal LastClose, decimal MarketValue);
}
