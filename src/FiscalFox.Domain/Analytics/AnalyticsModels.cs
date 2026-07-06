namespace FiscalFox.Domain.Analytics;

/// <summary>
/// A time series of closing prices for one instrument, ordered oldest-first.
/// The F# analytics engine consumes this shape.
/// </summary>
public record PriceSeries(string Symbol, IReadOnlyList<PricePoint> Points);

public record PricePoint(DateOnly Date, double Close);

/// <summary>Risk/return statistics for a single instrument or a whole portfolio.</summary>
public record RiskReturnStats(
    double MeanDailyReturn,
    double AnnualizedReturn,
    double DailyVolatility,
    double AnnualizedVolatility,
    double SharpeRatio,
    double MaxDrawdown,
    double ValueAtRisk95,
    int Observations);

/// <summary>One position's contribution to the portfolio, used for weights and rebalancing.</summary>
public record PositionValue(
    string Symbol,
    double Quantity,
    double Price,
    double MarketValue,
    double Weight,
    double? TargetWeight);

/// <summary>A single suggested rebalancing trade to reach the target allocation.</summary>
public record RebalanceAction(
    string Symbol,
    string Side,          // "Buy" or "Sell"
    double Units,
    double Amount,
    double CurrentWeight,
    double TargetWeight);

/// <summary>Full portfolio analytics snapshot returned by the API.</summary>
public record PortfolioReport(
    double TotalValue,
    IReadOnlyList<PositionValue> Positions,
    RiskReturnStats Risk,
    IReadOnlyList<RebalanceAction> Rebalance);
