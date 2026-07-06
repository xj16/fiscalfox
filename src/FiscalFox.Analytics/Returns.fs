namespace FiscalFox.Analytics

/// Return calculations over a series of closing prices.
module Returns =

    /// Simple (arithmetic) period-over-period returns from a price series.
    /// n prices produce n-1 returns. A zero previous price is skipped safely.
    let simpleReturns (prices: float list) : float list =
        match prices with
        | [] | [ _ ] -> []
        | _ ->
            prices
            |> List.pairwise
            |> List.map (fun (prev, curr) ->
                if prev = 0.0 then 0.0 else (curr - prev) / prev)

    /// Continuously-compounded (log) returns. Non-positive prices yield 0.0.
    let logReturns (prices: float list) : float list =
        match prices with
        | [] | [ _ ] -> []
        | _ ->
            prices
            |> List.pairwise
            |> List.map (fun (prev, curr) ->
                if prev <= 0.0 || curr <= 0.0 then 0.0 else log (curr / prev))

    /// Total return over the whole window: (last - first) / first.
    let totalReturn (prices: float list) : float =
        match prices with
        | [] | [ _ ] -> 0.0
        | first :: _ ->
            let last = List.last prices
            if first = 0.0 then 0.0 else (last - first) / first

    /// Cumulative wealth index starting at 1.0, compounding simple returns.
    /// Useful for drawdown and equity-curve charts.
    let cumulativeIndex (returns: float list) : float list =
        returns
        |> List.scan (fun acc r -> acc * (1.0 + r)) 1.0

    /// Annualize a mean daily (simple) return assuming <paramref name="periods"/>
    /// trading periods per year (default 252 for daily equity data).
    let annualizeReturn (periodsPerYear: float) (meanDailyReturn: float) : float =
        (1.0 + meanDailyReturn) ** periodsPerYear - 1.0

    /// Compound annual growth rate implied by a total return over a span of years.
    let cagr (totalRet: float) (years: float) : float =
        if years <= 0.0 then 0.0
        else (1.0 + totalRet) ** (1.0 / years) - 1.0
