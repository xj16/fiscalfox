namespace FiscalFox.Analytics.Tests

open FsCheck

/// FsCheck generators for "well-behaved" financial inputs: finite, positive
/// prices and bounded returns. This keeps property tests focused on real
/// numerical behavior rather than NaN/Infinity edge cases.
module Generators =

    /// A single positive price in a realistic range.
    let positivePrice : Gen<float> =
        Gen.choose (1, 1_000_000)
        |> Gen.map (fun c -> float c / 100.0) // 0.01 .. 10000.00

    /// A list of at least `minLen` positive prices.
    let priceListOfMin (minLen: int) : Gen<float list> =
        gen {
            let! n = Gen.choose (minLen, minLen + 60)
            let! prices = Gen.listOfLength n positivePrice
            return prices
        }

    /// A bounded daily return in [-0.5, 0.5].
    let boundedReturn : Gen<float> =
        Gen.choose (-5000, 5000) |> Gen.map (fun c -> float c / 10000.0)

    let returnList : Gen<float list> =
        gen {
            let! n = Gen.choose (2, 60)
            return! Gen.listOfLength n boundedReturn
        }

type PositivePrices = PositivePrices of float list
type ReturnSeries = ReturnSeries of float list

/// Arbitrary instances so [<Property>] can inject the custom generators.
type FinanceArbitraries =
    static member PositivePrices() : Arbitrary<PositivePrices> =
        Generators.priceListOfMin 2
        |> Gen.map PositivePrices
        |> Arb.fromGen

    static member ReturnSeries() : Arbitrary<ReturnSeries> =
        Generators.returnList
        |> Gen.map ReturnSeries
        |> Arb.fromGen
