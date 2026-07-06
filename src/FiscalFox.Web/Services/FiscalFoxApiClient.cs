using System.Net.Http.Json;
using System.Text.Json;
using FiscalFox.Domain.Analytics;

namespace FiscalFox.Web.Services;

/// <summary>
/// Thin typed wrapper over the FiscalFox REST API. All UI data flows through here.
/// </summary>
public class FiscalFoxApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public FiscalFoxApiClient(HttpClient http) => _http = http;

    public async Task<List<AccountVm>> GetAccountsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<AccountVm>>("api/accounts", JsonOpts, ct) ?? new();

    public async Task<List<HoldingVm>> GetHoldingsAsync(int accountId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<HoldingVm>>($"api/accounts/{accountId}/holdings", JsonOpts, ct) ?? new();

    public async Task<List<InstrumentVm>> GetInstrumentsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<InstrumentVm>>("api/instruments", JsonOpts, ct) ?? new();

    public async Task<List<PricePointVm>> GetPricesAsync(string symbol, int limit = 260, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<PricePointVm>>($"api/instruments/{symbol}/prices?limit={limit}", JsonOpts, ct) ?? new();

    public async Task<PortfolioReport?> GetPortfolioReportAsync(int accountId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<PortfolioReport>($"api/analytics/portfolio/{accountId}", JsonOpts, ct);

    public async Task<RiskReturnStats?> GetInstrumentRiskAsync(string symbol, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<RiskReturnStats>($"api/analytics/instrument/{symbol}", JsonOpts, ct);
}
