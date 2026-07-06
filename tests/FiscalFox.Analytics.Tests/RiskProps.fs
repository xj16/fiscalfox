namespace FiscalFox.Analytics.Tests

open FsCheck
open FsCheck.Xunit
open FiscalFox.Analytics

[<Properties(Arbitrary = [| typeof<FinanceArbitraries> |])>]
module RiskProps =

    [<Property>]
    let ``maxDrawdown is in [0, 1] for positive equity curves`` (PositivePrices ps) =
        // Treat a positive price series as an equity curve.
        let dd = Risk.maxDrawdown ps
        dd >= 0.0 && dd <= 1.0 + 1e-9

    [<Property>]
    let ``monotonically increasing curve has zero drawdown`` (n: PositiveInt) =
        let curve = [ for i in 1 .. n.Get + 1 -> float i ]
        Risk.maxDrawdown curve = 0.0

    [<Property>]
    let ``VaR is non-negative`` (ReturnSeries rs) =
        Risk.historicalVaR 0.95 rs >= 0.0

    [<Property>]
    let ``higher confidence never lowers VaR`` (ReturnSeries rs) =
        let v90 = Risk.historicalVaR 0.90 rs
        let v99 = Risk.historicalVaR 0.99 rs
        v99 >= v90 - 1e-9

    [<Property>]
    let ``annualized volatility scales daily vol by sqrt(252)`` (ReturnSeries rs) =
        let daily = Statistics.stdDev rs
        let ann = Risk.annualizeVolatility 252.0 daily
        abs (ann - daily * sqrt 252.0) < 1e-9

    [<Property>]
    let ``zero-volatility series gives zero Sharpe`` (price: PositiveInt) (n: PositiveInt) =
        let ps = List.replicate (n.Get + 2) (float price.Get)
        let rets = Returns.simpleReturns ps
        Risk.sharpeRatio 0.04 rets = 0.0

    [<Property>]
    let ``statsFromPrices reports a consistent observation count`` (PositivePrices ps) =
        let stats = Risk.statsFromPrices 0.04 ps
        stats.Observations = List.length ps - 1

    [<Property>]
    let ``statsFromPrices volatility is non-negative`` (PositivePrices ps) =
        let stats = Risk.statsFromPrices 0.04 ps
        stats.DailyVolatility >= 0.0 && stats.AnnualizedVolatility >= 0.0
