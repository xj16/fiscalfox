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

/// <summary>One point on the portfolio equity/drawdown curve (index base 1.0).</summary>
public record EquityPoint(int Index, double Equity, double Drawdown);

/// <summary>
/// The portfolio's value-weighted equity curve plus its drawdown series, ready
/// to plot. <see cref="MaxDrawdown"/> matches the report's risk figure.
/// </summary>
public record PortfolioTimeseries(
    IReadOnlyList<EquityPoint> Points,
    double MaxDrawdown,
    int Observations);

/// <summary>
/// A symbol-by-symbol Pearson correlation matrix over aligned daily returns.
/// <see cref="Matrix"/> is row-major and symmetric with a unit diagonal.
/// </summary>
public record CorrelationMatrix(
    IReadOnlyList<string> Symbols,
    IReadOnlyList<IReadOnlyList<double>> Matrix);

/// <summary>One sampled portfolio on the risk/return plane (annualized).</summary>
public record FrontierPoint(
    double Risk,
    double Return,
    double Sharpe,
    IReadOnlyList<double> Weights);

/// <summary>
/// A Markowitz efficient-frontier sample: many random long-only portfolios over
/// the given symbols, the max-Sharpe ("tangency") portfolio, and the current
/// portfolio plotted on the same axes.
/// </summary>
public record EfficientFrontier(
    IReadOnlyList<string> Symbols,
    IReadOnlyList<FrontierPoint> Samples,
    FrontierPoint MaxSharpe,
    FrontierPoint? Current);
