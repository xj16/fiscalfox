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

    // -----------------------------------------------------------------------
    // Transactions: the subsystem that drives holdings + cost basis.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Buy_then_sell_tracks_cost_basis_and_realized_pnl()
    {
        // Fresh account funded with cash.
        var account = await CreateAccountAsync(cash: 10_000m);

        // Buy 10 @ 100 (+1 fee) -> avg cost = (10*100 + 1)/10 = 100.1
        var buy = await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new
        {
            kind = 0, symbol = "VTI", quantity = 10m, price = 100m, fee = 1m
        });
        Assert.Equal(HttpStatusCode.Created, buy.StatusCode);

        var afterBuy = await GetHoldingAsync(account.Id, "VTI");
        Assert.Equal(10m, afterBuy.Quantity);
        Assert.Equal(100.1m, afterBuy.AverageCost);

        // Sell 4 @ 150 (-1 fee) -> realized = 4*(150-100.1) - 1 = 4*49.9 - 1 = 198.6
        var sell = await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new
        {
            kind = 1, symbol = "VTI", quantity = 4m, price = 150m, fee = 1m
        });
        Assert.Equal(HttpStatusCode.Created, sell.StatusCode);

        var afterSell = await GetHoldingAsync(account.Id, "VTI");
        Assert.Equal(6m, afterSell.Quantity);
        Assert.Equal(100.1m, afterSell.AverageCost); // basis unchanged by a sell
        Assert.Equal(198.6m, afterSell.RealizedPnL);

        // Cash: 10000 - (10*100+1) + (4*150-1) = 10000 - 1001 + 599 = 9598
        var reloaded = await _client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{account.Id}", Json);
        Assert.Equal(9598m, reloaded!.CashBalance);
    }

    [Fact]
    public async Task Overselling_a_position_is_rejected()
    {
        var account = await CreateAccountAsync(cash: 5_000m);
        await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new
        {
            kind = 0, symbol = "BND", quantity = 3m, price = 70m, fee = 0m
        });

        var oversell = await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new
        {
            kind = 1, symbol = "BND", quantity = 99m, price = 70m, fee = 0m
        });
        Assert.Equal(HttpStatusCode.BadRequest, oversell.StatusCode);
    }

    [Fact]
    public async Task Deposit_and_withdrawal_move_cash_and_guard_overdraft()
    {
        var account = await CreateAccountAsync(cash: 0m);

        var deposit = await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new
        {
            kind = 2, price = 500m, quantity = 0m, fee = 0m // Deposit
        });
        Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);

        var afterDeposit = await _client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{account.Id}", Json);
        Assert.Equal(500m, afterDeposit!.CashBalance);

        var overdraft = await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new
        {
            kind = 3, price = 9_999m, quantity = 0m, fee = 0m // Withdrawal beyond balance
        });
        Assert.Equal(HttpStatusCode.BadRequest, overdraft.StatusCode);
    }

    [Fact]
    public async Task Transaction_history_lists_newest_first()
    {
        var account = await CreateAccountAsync(cash: 10_000m);
        await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new
        {
            kind = 0, symbol = "VTI", quantity = 1m, price = 100m, fee = 0m
        });
        await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new
        {
            kind = 0, symbol = "GLD", quantity = 1m, price = 120m, fee = 0m
        });

        var txs = await _client.GetFromJsonAsync<List<TransactionResponse>>(
            $"/api/accounts/{account.Id}/transactions", Json);
        Assert.NotNull(txs);
        Assert.True(txs!.Count >= 2);
        // Newest first.
        Assert.True(txs[0].TimestampUtc >= txs[1].TimestampUtc);
    }

    [Fact]
    public async Task Transactions_on_missing_account_return_404()
    {
        var resp = await _client.GetAsync("/api/accounts/999999/transactions");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // -----------------------------------------------------------------------
    // New analytics endpoints: timeseries, correlation, frontier.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Timeseries_endpoint_returns_equity_curve_anchored_at_one()
    {
        var accounts = await _client.GetFromJsonAsync<List<AccountResponse>>("/api/accounts", Json);
        var id = accounts![0].Id;

        var ts = await _client.GetFromJsonAsync<PortfolioTimeseries>(
            $"/api/analytics/portfolio/{id}/timeseries", Json);
        Assert.NotNull(ts);
        Assert.NotEmpty(ts!.Points);
        Assert.Equal(1.0, ts.Points[0].Equity, 6);
        Assert.InRange(ts.MaxDrawdown, 0.0, 1.0);
    }

    [Fact]
    public async Task Correlation_matrix_is_square_symmetric_with_unit_diagonal()
    {
        var m = await _client.GetFromJsonAsync<CorrelationMatrix>("/api/analytics/correlation", Json);
        Assert.NotNull(m);
        var n = m!.Symbols.Count;
        Assert.True(n >= 2);
        Assert.Equal(n, m.Matrix.Count);
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(n, m.Matrix[i].Count);
            Assert.Equal(1.0, m.Matrix[i][i], 6); // unit diagonal
            for (var j = 0; j < n; j++)
            {
                Assert.Equal(m.Matrix[i][j], m.Matrix[j][i], 6); // symmetric
                Assert.InRange(m.Matrix[i][j], -1.0001, 1.0001);
            }
        }
    }

    [Fact]
    public async Task Frontier_endpoint_returns_samples_and_places_current_portfolio()
    {
        var accounts = await _client.GetFromJsonAsync<List<AccountResponse>>("/api/accounts", Json);
        var id = accounts![0].Id;

        var f = await _client.GetFromJsonAsync<EfficientFrontier>(
            $"/api/analytics/frontier/{id}?samples=300", Json);
        Assert.NotNull(f);
        Assert.NotEmpty(f!.Samples);
        // Long-only weights on every sample.
        foreach (var s in f.Samples)
            Assert.InRange(s.Weights.Sum(), 0.999, 1.001);
        // Max-Sharpe dominates the cloud.
        Assert.True(f.Samples.All(s => f.MaxSharpe.Sharpe >= s.Sharpe - 1e-6));
        // The seeded demo account is invested, so its current point is placed.
        Assert.NotNull(f.Current);
    }

    // -----------------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------------

    private async Task<AccountResponse> CreateAccountAsync(decimal cash)
    {
        var create = await _client.PostAsJsonAsync("/api/accounts", new
        {
            name = $"Tx Test {Guid.NewGuid():N}", currency = "USD", type = 0, cashBalance = cash
        });
        create.EnsureSuccessStatusCode();
        return (await create.Content.ReadFromJsonAsync<AccountResponse>(Json))!;
    }

    private async Task<HoldingResponse> GetHoldingAsync(int accountId, string symbol)
    {
        var holdings = await _client.GetFromJsonAsync<List<HoldingResponse>>(
            $"/api/accounts/{accountId}/holdings", Json);
        return holdings!.Single(h => h.Symbol == symbol);
    }

    // Local response shapes for deserialization.
    private record AccountResponse(int Id, string Name, string Currency, int Type, decimal CashBalance);
    private record InstrumentResponse(int Id, string Symbol, string Name, int AssetClass, string Currency, decimal? LastClose);
    private record HoldingResponse(int Id, int InstrumentId, string Symbol, decimal Quantity, decimal AverageCost, decimal? TargetWeight, decimal LastClose, decimal MarketValue, decimal RealizedPnL, decimal UnrealizedPnL);
    private record TransactionResponse(long Id, int Kind, string? Symbol, decimal Quantity, decimal Price, decimal CashImpact, decimal Fee, DateTime TimestampUtc, string? Note);
}
