namespace FiscalFox.Domain.Entities;

/// <summary>
/// A tradable instrument (stock, ETF, crypto). Price history is cached locally
/// in <see cref="PriceBar"/> so the app never needs a paid market-data API.
/// </summary>
public class Instrument
{
    public int Id { get; set; }

    /// <summary>Ticker symbol, e.g. "VTI" or "BTC-USD".</summary>
    public string Symbol { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public AssetClass AssetClass { get; set; } = AssetClass.Equity;

    /// <summary>ISO-4217 quote currency.</summary>
    public string Currency { get; set; } = "USD";

    public List<PriceBar> Prices { get; set; } = new();
    public List<Holding> Holdings { get; set; } = new();
}

public enum AssetClass
{
    Equity = 0,
    Bond = 1,
    Cash = 2,
    Crypto = 3,
    Commodity = 4,
    RealEstate = 5
}
