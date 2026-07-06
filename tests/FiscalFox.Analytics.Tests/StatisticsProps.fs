namespace FiscalFox.Analytics.Tests

open FsCheck
open FsCheck.Xunit
open FiscalFox.Analytics

[<Properties(Arbitrary = [| typeof<FinanceArbitraries> |])>]
module StatisticsProps =

    let private approx (a: float) (b: float) = abs (a - b) < 1e-6

    [<Property>]
    let ``mean lies between min and max`` (ReturnSeries xs) =
        not (List.isEmpty xs) ==> lazy (
            let m = Statistics.mean xs
            m >= List.min xs - 1e-9 && m <= List.max xs + 1e-9)

    [<Property>]
    let ``variance is non-negative`` (ReturnSeries xs) =
        Statistics.variance xs >= 0.0

    [<Property>]
    let ``stdDev is sqrt of variance`` (ReturnSeries xs) =
        approx (Statistics.stdDev xs) (sqrt (Statistics.variance xs))

    [<Property>]
    let ``variance of a constant series is zero`` (c: int) (n: PositiveInt) =
        // Bound the constant so huge-magnitude floats don't introduce rounding noise.
        let value = float (c % 100_000)
        let xs = List.replicate (n.Get + 1) value
        approx (Statistics.variance xs) 0.0

    [<Property>]
    let ``correlation is bounded in [-1, 1]`` (ReturnSeries xs) (ReturnSeries ys) =
        let r = Statistics.correlation xs ys
        r >= -1.0 - 1e-9 && r <= 1.0 + 1e-9

    [<Property>]
    let ``self-correlation is 1 for non-constant series`` (ReturnSeries xs) =
        (Statistics.stdDev xs > 1e-6) ==> lazy (
            approx (Statistics.correlation xs xs) 1.0)

    [<Property>]
    let ``percentile is monotic non-decreasing in p`` (ReturnSeries xs) =
        not (List.isEmpty xs) ==> lazy (
            let a = Statistics.percentile 0.10 xs
            let b = Statistics.percentile 0.90 xs
            a <= b + 1e-9)

    [<Property>]
    let ``percentile stays within data range`` (ReturnSeries xs) (p: NormalFloat) =
        not (List.isEmpty xs) ==> lazy (
            let frac = abs p.Get - floor (abs p.Get) // in [0,1)
            let q = Statistics.percentile frac xs
            q >= List.min xs - 1e-9 && q <= List.max xs + 1e-9)
