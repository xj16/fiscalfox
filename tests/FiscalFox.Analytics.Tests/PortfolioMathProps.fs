namespace FiscalFox.Analytics.Tests

open Xunit
open FsCheck
open FsCheck.Xunit
open FiscalFox.Analytics

/// Property + example tests for the cross-asset analytics: the correlation
/// matrix and the Markowitz efficient-frontier sampler. These cover the hardest,
/// most-advertised part of the engine — the multi-asset math that drives the
/// dashboard's heatmap and risk/return scatter.
module PortfolioMathProps =

    /// Generate 2..5 assets, each with 30..90 positive prices.
    let private assetsGen : Gen<PortfolioMath.AssetSeries list> =
        gen {
            let! n = Gen.choose (2, 5)
            let! assets =
                Gen.listOfLength n (
                    gen {
                        let! prices = Generators.priceListOfMin 30
                        return prices
                    })
            return
                assets
                |> List.mapi (fun i p ->
                    { PortfolioMath.AssetSeries.Symbol = sprintf "A%d" i
                      PortfolioMath.AssetSeries.Prices = p })
        }

    type MathArb =
        static member Assets() : Arbitrary<PortfolioMath.AssetSeries list> =
            Arb.fromGen assetsGen

    // ---- Correlation matrix ------------------------------------------------

    [<Property(Arbitrary = [| typeof<MathArb> |])>]
    let ``correlation matrix is square with one row per asset`` (assets: PortfolioMath.AssetSeries list) =
        let m = PortfolioMath.correlationMatrix assets
        let n = m.Symbols.Count
        m.Matrix.Count = n && (m.Matrix |> Seq.forall (fun row -> row.Count = n))

    [<Property(Arbitrary = [| typeof<MathArb> |])>]
    let ``correlation matrix has a unit diagonal`` (assets: PortfolioMath.AssetSeries list) =
        let m = PortfolioMath.correlationMatrix assets
        [ 0 .. m.Symbols.Count - 1 ]
        |> List.forall (fun i -> abs (m.Matrix.[i].[i] - 1.0) < 1e-9)

    [<Property(Arbitrary = [| typeof<MathArb> |])>]
    let ``correlation matrix is symmetric`` (assets: PortfolioMath.AssetSeries list) =
        let m = PortfolioMath.correlationMatrix assets
        let n = m.Symbols.Count
        seq {
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 -> abs (m.Matrix.[i].[j] - m.Matrix.[j].[i]) < 1e-9
        }
        |> Seq.forall id

    [<Property(Arbitrary = [| typeof<MathArb> |])>]
    let ``every correlation entry is within [-1, 1]`` (assets: PortfolioMath.AssetSeries list) =
        let m = PortfolioMath.correlationMatrix assets
        m.Matrix
        |> Seq.collect id
        |> Seq.forall (fun v -> v >= -1.0 - 1e-9 && v <= 1.0 + 1e-9)

    [<Fact>]
    let ``assets with identical daily returns read ~1`` () =
        // Correlation is over daily RETURNS, so proportional price series (b = 3a)
        // have identical returns and must correlate to ~1.
        let basePrices = [ 100.0; 103.0; 101.0; 108.0; 111.0; 105.0; 120.0; 118.0; 125.0; 130.0 ]
        let a = { PortfolioMath.AssetSeries.Symbol = "A"
                  PortfolioMath.AssetSeries.Prices = basePrices }
        let b = { PortfolioMath.AssetSeries.Symbol = "B"
                  PortfolioMath.AssetSeries.Prices = basePrices |> List.map (fun p -> 3.0 * p) }
        let m = PortfolioMath.correlationMatrix [ a; b ]
        Assert.True(m.Matrix.[0].[1] > 0.999, sprintf "off-diagonal was %f" m.Matrix.[0].[1])

    // ---- Efficient frontier ------------------------------------------------

    [<Property(Arbitrary = [| typeof<MathArb> |])>]
    let ``frontier weights are long-only and sum to 1`` (assets: PortfolioMath.AssetSeries list) =
        let f = PortfolioMath.efficientFrontier 0.04 200 7 None assets
        f.Samples
        |> Seq.forall (fun p ->
            let ws = List.ofSeq p.Weights
            let total = List.sum ws
            abs (total - 1.0) < 1e-6 && List.forall (fun w -> w >= -1e-9) ws)

    [<Property(Arbitrary = [| typeof<MathArb> |])>]
    let ``frontier risk is non-negative and finite`` (assets: PortfolioMath.AssetSeries list) =
        let f = PortfolioMath.efficientFrontier 0.04 200 7 None assets
        f.Samples
        |> Seq.forall (fun p ->
            p.Risk >= 0.0 && not (System.Double.IsNaN p.Risk) && not (System.Double.IsInfinity p.Risk))

    [<Property(Arbitrary = [| typeof<MathArb> |])>]
    let ``max-Sharpe portfolio dominates every sample on Sharpe`` (assets: PortfolioMath.AssetSeries list) =
        let f = PortfolioMath.efficientFrontier 0.04 200 7 None assets
        f.Samples |> Seq.forall (fun p -> f.MaxSharpe.Sharpe >= p.Sharpe - 1e-9)

    [<Property(Arbitrary = [| typeof<MathArb> |])>]
    let ``frontier is deterministic for a fixed seed`` (assets: PortfolioMath.AssetSeries list) =
        let f1 = PortfolioMath.efficientFrontier 0.04 150 42 None assets
        let f2 = PortfolioMath.efficientFrontier 0.04 150 42 None assets
        // Same seed -> identical max-Sharpe risk/return.
        abs (f1.MaxSharpe.Risk - f2.MaxSharpe.Risk) < 1e-12
        && abs (f1.MaxSharpe.Return - f2.MaxSharpe.Return) < 1e-12

    [<Fact>]
    let ``current portfolio is placed when weights are supplied`` () =
        let mk s p = { PortfolioMath.AssetSeries.Symbol = s; PortfolioMath.AssetSeries.Prices = p }
        let assets =
            [ mk "A" [ for i in 1 .. 40 -> 100.0 + float i ]
              mk "B" [ for i in 1 .. 40 -> 50.0 + 0.5 * float i ] ]
        let f = PortfolioMath.efficientFrontier 0.04 100 1 (Some [ 0.6; 0.4 ]) assets
        Assert.NotNull(box f.Current)
        Assert.True(f.Current.Risk >= 0.0)

    // ---- Portfolio.report aggregation (the trickiest, value-weighted code) --

    /// Generate 1..4 holdings with positive quantities and >= 20 positive prices.
    let private holdingsGen : Gen<Portfolio.HoldingInput list> =
        gen {
            let! n = Gen.choose (1, 4)
            let! hs =
                Gen.listOfLength n (
                    gen {
                        let! qty = Gen.choose (1, 500) |> Gen.map float
                        let! prices = Generators.priceListOfMin 20
                        return qty, prices
                    })
            return
                hs
                |> List.mapi (fun i (qty, prices) ->
                    { Portfolio.HoldingInput.Symbol = sprintf "H%d" i
                      Portfolio.HoldingInput.Quantity = qty
                      Portfolio.HoldingInput.Prices = prices
                      Portfolio.HoldingInput.TargetWeight = None })
        }

    type HoldingArb =
        static member Holdings() : Arbitrary<Portfolio.HoldingInput list> =
            Arb.fromGen holdingsGen

    [<Property(Arbitrary = [| typeof<HoldingArb> |])>]
    let ``position weights sum to ~1 for a non-empty portfolio`` (holdings: Portfolio.HoldingInput list) =
        let report = Portfolio.report 0.04 50.0 holdings
        report.TotalValue > 0.0 ==> lazy (
            let sum = report.Positions |> Seq.sumBy (fun p -> p.Weight)
            abs (sum - 1.0) < 1e-6)

    [<Property(Arbitrary = [| typeof<HoldingArb> |])>]
    let ``report total value is order-invariant`` (holdings: Portfolio.HoldingInput list) =
        let forward = Portfolio.report 0.04 50.0 holdings
        let reversed = Portfolio.report 0.04 50.0 (List.rev holdings)
        abs (forward.TotalValue - reversed.TotalValue) < 1e-6

    [<Property(Arbitrary = [| typeof<HoldingArb> |])>]
    let ``report risk stats are order-invariant`` (holdings: Portfolio.HoldingInput list) =
        let forward = Portfolio.report 0.04 50.0 holdings
        let reversed = Portfolio.report 0.04 50.0 (List.rev holdings)
        // Value-weighting is commutative, so aggregate risk must not depend on order.
        abs (forward.Risk.AnnualizedVolatility - reversed.Risk.AnnualizedVolatility) < 1e-9
        && abs (forward.Risk.MaxDrawdown - reversed.Risk.MaxDrawdown) < 1e-9
        && forward.Risk.Observations = reversed.Risk.Observations

    [<Property(Arbitrary = [| typeof<HoldingArb> |])>]
    let ``timeseries observation count equals report risk observations`` (holdings: Portfolio.HoldingInput list) =
        let report = Portfolio.report 0.04 50.0 holdings
        let ts = Portfolio.timeseries holdings
        ts.Observations = report.Risk.Observations
