namespace FiscalFox.Analytics

open System.Collections.Generic
open FiscalFox.Domain.Analytics

/// Top-level facade the C# Web API calls into. Combines returns, risk and
/// rebalancing into a single <see cref="PortfolioReport"/>.
module Portfolio =

    /// One holding plus the price history needed to value and analyze it.
    type HoldingInput =
        { Symbol: string
          Quantity: float
          /// Closing prices oldest-first. The last element is the current price.
          Prices: float list
          TargetWeight: float option }

    let private lastPrice (prices: float list) : float =
        match prices with
        | [] -> 0.0
        | _ -> List.last prices

    /// Value each holding and compute portfolio weights.
    let positionValues (holdings: HoldingInput list) : PositionValue list =
        let valued =
            holdings
            |> List.map (fun h ->
                let price = lastPrice h.Prices
                h, price, h.Quantity * price)
        let total = valued |> List.sumBy (fun (_, _, mv) -> mv)
        valued
        |> List.map (fun (h, price, mv) ->
            let weight = if total > 0.0 then mv / total else 0.0
            PositionValue(
                Symbol = h.Symbol,
                Quantity = h.Quantity,
                Price = price,
                MarketValue = mv,
                Weight = weight,
                TargetWeight = (h.TargetWeight |> Option.map float |> Option.toNullable)))

    /// Aggregate portfolio daily returns by value-weighting each holding's returns.
    /// Series are aligned on their trailing overlap so uneven history never throws.
    let private portfolioReturns (holdings: HoldingInput list) : float list =
        let withReturns =
            holdings
            |> List.map (fun h -> h, lastPrice h.Prices * h.Quantity, Returns.simpleReturns h.Prices)
            |> List.filter (fun (_, mv, rets) -> mv > 0.0 && not (List.isEmpty rets))
        match withReturns with
        | [] -> []
        | _ ->
            let totalValue = withReturns |> List.sumBy (fun (_, mv, _) -> mv)
            let minLen = withReturns |> List.map (fun (_, _, r) -> List.length r) |> List.min
            // Value-weight each aligned daily return.
            [ for i in 0 .. minLen - 1 ->
                withReturns
                |> List.sumBy (fun (_, mv, rets) ->
                    // Align to the tail so the most recent returns overlap.
                    let offset = List.length rets - minLen
                    let w = mv / totalValue
                    w * rets.[offset + i]) ]

    /// Build the full analytics report for a set of holdings.
    /// <paramref name="annualRiskFree"/> feeds the Sharpe ratio (e.g. 0.04 for 4%).
    /// <paramref name="minTrade"/> suppresses rebalance trades below that cash size.
    let report (annualRiskFree: float) (minTrade: float) (holdings: HoldingInput list) : PortfolioReport =
        let positions = positionValues holdings
        let totalValue = positions |> List.sumBy (fun p -> p.MarketValue)

        let portRets = portfolioReturns holdings
        let equity = Returns.cumulativeIndex portRets
        let risk =
            RiskReturnStats(
                MeanDailyReturn = Statistics.mean portRets,
                AnnualizedReturn = Returns.annualizeReturn Risk.TradingDays (Statistics.mean portRets),
                DailyVolatility = Statistics.stdDev portRets,
                AnnualizedVolatility = Risk.annualizeVolatility Risk.TradingDays (Statistics.stdDev portRets),
                SharpeRatio = Risk.sharpeRatio annualRiskFree portRets,
                MaxDrawdown = Risk.maxDrawdown equity,
                ValueAtRisk95 = Risk.historicalVaR 0.95 portRets,
                Observations = List.length portRets)

        let rebalInputs =
            holdings
            |> List.map (fun h ->
                { Rebalancing.PositionInput.Symbol = h.Symbol
                  Rebalancing.PositionInput.Quantity = h.Quantity
                  Rebalancing.PositionInput.Price = lastPrice h.Prices
                  Rebalancing.PositionInput.TargetWeight = h.TargetWeight })
        let rebalance = Rebalancing.plan minTrade rebalInputs

        // Arrays implement IReadOnlyList<'T> (F# lists do not), so materialize.
        PortfolioReport(
            TotalValue = totalValue,
            Positions = (List.toArray positions :> IReadOnlyList<PositionValue>),
            Risk = risk,
            Rebalance = (List.toArray rebalance :> IReadOnlyList<RebalanceAction>))
