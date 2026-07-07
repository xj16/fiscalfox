namespace FiscalFox.Analytics

open System.Collections.Generic
open FiscalFox.Domain.Analytics

/// Portfolio-level cross-asset analytics built on the (already property-tested)
/// primitives in <see cref="Statistics"/> and <see cref="Returns"/>:
///   * a symbol-by-symbol correlation matrix, and
///   * a Markowitz efficient-frontier sampler.
///
/// Everything here is a pure function of price histories and (optionally) a set
/// of weights, so it property-tests as cleanly as the rest of the engine.
module PortfolioMath =

    /// One asset's identity plus the closing prices used to derive its returns.
    type AssetSeries =
        { Symbol: string
          /// Closing prices oldest-first (>= 2 to yield returns).
          Prices: float list }

    /// Align a set of daily-return series to their common trailing length so
    /// every pairwise statistic is computed over the exact same observations.
    /// Returns (symbols, alignedReturns) with each return list the same length.
    let private alignReturns (assets: AssetSeries list) : string list * float list list =
        let withRets =
            assets
            |> List.map (fun a -> a.Symbol, Returns.simpleReturns a.Prices)
            |> List.filter (fun (_, r) -> not (List.isEmpty r))
        match withRets with
        | [] -> [], []
        | _ ->
            let minLen = withRets |> List.map (snd >> List.length) |> List.min
            let symbols = withRets |> List.map fst
            // Tail-align: keep the most recent `minLen` returns of each series.
            let aligned =
                withRets
                |> List.map (fun (_, r) ->
                    let offset = List.length r - minLen
                    r |> List.skip offset)
            symbols, aligned

    /// Build a Pearson correlation matrix over the assets' aligned daily returns.
    /// The result is symmetric with a unit diagonal (degenerate series read 0).
    let correlationMatrix (assets: AssetSeries list) : CorrelationMatrix =
        let symbols, aligned = alignReturns assets
        let n = List.length symbols
        let arr = List.toArray aligned
        let rows =
            [ for i in 0 .. n - 1 ->
                [ for j in 0 .. n - 1 ->
                    if i = j then 1.0
                    else Statistics.correlation arr.[i] arr.[j] ]
                :> IReadOnlyList<float> ]
        CorrelationMatrix(
            Symbols = (List.toArray symbols :> IReadOnlyList<string>),
            Matrix = (List.toArray rows :> IReadOnlyList<IReadOnlyList<float>>))

    /// Annualized mean return and volatility of a weighted combination of the
    /// aligned return series. Weights are assumed to sum to 1.
    let private annualizedRiskReturn
        (aligned: float[][])
        (weights: float[])
        : float * float =
        let obs = if aligned.Length = 0 then 0 else aligned.[0].Length
        if obs = 0 then
            0.0, 0.0
        else
            // Portfolio daily returns = weighted sum across assets per period.
            let portfolio =
                [| for t in 0 .. obs - 1 ->
                     let mutable acc = 0.0
                     for k in 0 .. aligned.Length - 1 do
                         acc <- acc + weights.[k] * aligned.[k].[t]
                     acc |]
                |> Array.toList
            let annReturn =
                Returns.annualizeReturn Risk.TradingDays (Statistics.mean portfolio)
            let annVol =
                Risk.annualizeVolatility Risk.TradingDays (Statistics.stdDev portfolio)
            annReturn, annVol

    /// Coerce a non-finite float (NaN/Infinity) to 0.0 so a pathological input
    /// series can never poison a comparison or a max/min selection downstream.
    let private finite (x: float) : float =
        if System.Double.IsNaN x || System.Double.IsInfinity x then 0.0 else x

    /// Score a specific weight vector on the annualized risk/return plane.
    let private evaluate
        (aligned: float[][])
        (annualRiskFree: float)
        (symbols: string list)
        (weights: float[])
        : FrontierPoint =
        let annReturn, annVol = annualizedRiskReturn aligned weights
        let annReturn = finite annReturn
        let annVol = finite annVol
        let sharpe = if annVol > 0.0 then finite ((annReturn - annualRiskFree) / annVol) else 0.0
        FrontierPoint(
            Risk = annVol,
            Return = annReturn,
            Sharpe = sharpe,
            Weights = (Array.toList weights :> IReadOnlyList<float>))

    /// Draw `samples` random long-only weight vectors (Dirichlet-ish via
    /// normalized exponentials) and evaluate each. Deterministic for a given
    /// seed so the frontier is reproducible and testable.
    let efficientFrontier
        (annualRiskFree: float)
        (samples: int)
        (seed: int)
        (currentWeights: float list option)
        (assets: AssetSeries list)
        : EfficientFrontier =
        let symbols, alignedList = alignReturns assets
        let aligned = alignedList |> List.map List.toArray |> List.toArray
        let n = List.length symbols
        let rnd = System.Random(seed)

        // A single random long-only weight vector summing to 1.
        let randomWeights () : float[] =
            let raw = Array.init n (fun _ -> -log (1.0 - rnd.NextDouble()))
            let total = Array.sum raw
            if total <= 0.0 then Array.create n (1.0 / float (max 1 n))
            else raw |> Array.map (fun w -> w / total)

        let sampleList =
            if n = 0 then []
            else
                [ for _ in 1 .. max 1 samples ->
                    evaluate aligned annualRiskFree symbols (randomWeights ()) ]

        // Always include the two single-name / equal-weight anchors so the hull
        // is well-formed even with few samples.
        let anchors =
            if n = 0 then []
            else
                let equal = Array.create n (1.0 / float n)
                let singles = [ for i in 0 .. n - 1 -> Array.init n (fun j -> if j = i then 1.0 else 0.0) ]
                (equal :: singles) |> List.map (evaluate aligned annualRiskFree symbols)

        let allSamples = sampleList @ anchors

        let maxSharpe =
            match allSamples with
            | [] -> FrontierPoint(Risk = 0.0, Return = 0.0, Sharpe = 0.0, Weights = ([] :> IReadOnlyList<float>))
            | _ -> allSamples |> List.maxBy (fun p -> p.Sharpe)

        // Current is a nullable reference in the C# DTO; map None -> null.
        let current : FrontierPoint =
            match currentWeights with
            | Some w when List.length w = n && n > 0 ->
                evaluate aligned annualRiskFree symbols (List.toArray w)
            | _ -> Unchecked.defaultof<FrontierPoint>

        EfficientFrontier(
            Symbols = (List.toArray symbols :> IReadOnlyList<string>),
            Samples = (List.toArray allSamples :> IReadOnlyList<FrontierPoint>),
            MaxSharpe = maxSharpe,
            Current = current)
