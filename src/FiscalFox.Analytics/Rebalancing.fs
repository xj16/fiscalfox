namespace FiscalFox.Analytics

open FiscalFox.Domain.Analytics

/// Portfolio rebalancing: turn current weights + targets into concrete trades.
module Rebalancing =

    /// A minimal position input for the rebalancer.
    type PositionInput =
        { Symbol: string
          Quantity: float
          Price: float
          TargetWeight: float option }

    /// Normalize a list of target weights so the specified ones sum to 1.0.
    /// Positions without a target are treated as target 0 (fully divested).
    let private normalizedTargets (positions: PositionInput list) : Map<string, float> =
        let totalTarget =
            positions
            |> List.sumBy (fun p -> defaultArg p.TargetWeight 0.0)
        positions
        |> List.map (fun p ->
            let raw = defaultArg p.TargetWeight 0.0
            let norm = if totalTarget > 0.0 then raw / totalTarget else 0.0
            p.Symbol, norm)
        |> Map.ofList

    /// Current market value of a position.
    let private marketValue (p: PositionInput) : float = p.Quantity * p.Price

    /// Compute the list of trades needed to move from current to target weights.
    /// Only trades whose absolute cash amount exceeds <paramref name="minTrade"/>
    /// are emitted, avoiding churn on rounding noise. Prices must be positive.
    let plan (minTrade: float) (positions: PositionInput list) : RebalanceAction list =
        let totalValue = positions |> List.sumBy marketValue
        if totalValue <= 0.0 then
            []
        else
            let targets = normalizedTargets positions
            positions
            |> List.choose (fun p ->
                let mv = marketValue p
                let currentWeight = mv / totalValue
                let targetWeight = Map.tryFind p.Symbol targets |> Option.defaultValue 0.0
                let targetValue = targetWeight * totalValue
                let delta = targetValue - mv // positive => buy more
                if abs delta < minTrade || p.Price <= 0.0 then
                    None
                else
                    let side = if delta > 0.0 then "Buy" else "Sell"
                    let units = abs delta / p.Price
                    Some(
                        RebalanceAction(
                            Symbol = p.Symbol,
                            Side = side,
                            Units = units,
                            Amount = abs delta,
                            CurrentWeight = currentWeight,
                            TargetWeight = targetWeight)))

    /// Total absolute turnover (sum of trade amounts) a plan would incur.
    let turnover (actions: RebalanceAction list) : float =
        actions |> List.sumBy (fun a -> a.Amount)
