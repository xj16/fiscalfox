namespace FiscalFox.Analytics

open FiscalFox.Domain.Analytics

/// Risk metrics: volatility, drawdown, Value-at-Risk, Sharpe ratio.
module Risk =

    /// Trading periods per year assumed for daily data.
    [<Literal>]
    let TradingDays = 252.0

    /// Annualize a daily volatility (std dev of daily returns).
    let annualizeVolatility (periodsPerYear: float) (dailyVol: float) : float =
        dailyVol * sqrt periodsPerYear

    /// Maximum drawdown of a wealth/equity curve, returned as a POSITIVE fraction
    /// (e.g. 0.30 == a 30% peak-to-trough decline). 0.0 for empty/monotone-up curves.
    let maxDrawdown (equityCurve: float list) : float =
        match equityCurve with
        | [] -> 0.0
        | _ ->
            let mutable peak = System.Double.NegativeInfinity
            let mutable maxDd = 0.0
            for v in equityCurve do
                if v > peak then peak <- v
                if peak > 0.0 then
                    let dd = (peak - v) / peak
                    if dd > maxDd then maxDd <- dd
            maxDd

    /// Historical Value-at-Risk at the given confidence (e.g. 0.95).
    /// Returned as a POSITIVE fraction representing the loss threshold:
    /// "on the worst 5% of days we lost at least this much".
    let historicalVaR (confidence: float) (returns: float list) : float =
        match returns with
        | [] -> 0.0
        | _ ->
            let q = Statistics.percentile (1.0 - confidence) returns
            max 0.0 (-q)

    /// Sharpe ratio from a daily-return series and an ANNUAL risk-free rate.
    /// Annualized. Returns 0.0 when volatility is zero.
    let sharpeRatio (annualRiskFree: float) (returns: float list) : float =
        match returns with
        | [] | [ _ ] -> 0.0
        | _ ->
            let meanDaily = Statistics.mean returns
            let volDaily = Statistics.stdDev returns
            if volDaily = 0.0 then
                0.0
            else
                let annualReturn = Returns.annualizeReturn TradingDays meanDaily
                let annualVol = annualizeVolatility TradingDays volDaily
                (annualReturn - annualRiskFree) / annualVol

    /// Compute the full <see cref="RiskReturnStats"/> record from a price series.
    /// <paramref name="annualRiskFree"/> is used for the Sharpe ratio.
    let statsFromPrices (annualRiskFree: float) (prices: float list) : RiskReturnStats =
        let rets = Returns.simpleReturns prices
        let meanDaily = Statistics.mean rets
        let volDaily = Statistics.stdDev rets
        let equity = Returns.cumulativeIndex rets
        RiskReturnStats(
            MeanDailyReturn = meanDaily,
            AnnualizedReturn = Returns.annualizeReturn TradingDays meanDaily,
            DailyVolatility = volDaily,
            AnnualizedVolatility = annualizeVolatility TradingDays volDaily,
            SharpeRatio = sharpeRatio annualRiskFree rets,
            MaxDrawdown = maxDrawdown equity,
            ValueAtRisk95 = historicalVaR 0.95 rets,
            Observations = List.length rets)
