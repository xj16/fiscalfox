namespace FiscalFox.Analytics.Tests

open FsCheck
open FsCheck.Xunit
open FiscalFox.Analytics

[<Properties(Arbitrary = [| typeof<FinanceArbitraries> |])>]
module ReturnsProps =

    let private approx tol (a: float) (b: float) = abs (a - b) < tol

    [<Property>]
    let ``simpleReturns has one fewer element than prices`` (PositivePrices ps) =
        List.length (Returns.simpleReturns ps) = List.length ps - 1

    [<Property>]
    let ``logReturns has one fewer element than prices`` (PositivePrices ps) =
        List.length (Returns.logReturns ps) = List.length ps - 1

    [<Property>]
    let ``constant price series yields zero returns`` (price: PositiveInt) (n: PositiveInt) =
        let ps = List.replicate (n.Get + 1) (float price.Get)
        Returns.simpleReturns ps |> List.forall (fun r -> approx 1e-9 r 0.0)

    [<Property>]
    let ``cumulative index compounds to total return`` (PositivePrices ps) =
        let rets = Returns.simpleReturns ps
        let idx = Returns.cumulativeIndex rets
        let compounded = List.last idx - 1.0
        approx 1e-6 compounded (Returns.totalReturn ps)

    [<Property>]
    let ``cumulativeIndex starts at 1`` (ReturnSeries rs) =
        let idx = Returns.cumulativeIndex rs
        approx 1e-12 (List.head idx) 1.0

    [<Property>]
    let ``log and simple returns agree via exp`` (PositivePrices ps) =
        let simple = Returns.simpleReturns ps
        let logs = Returns.logReturns ps
        List.zip simple logs
        |> List.forall (fun (s, l) -> approx 1e-6 (log (1.0 + s)) l)

    [<Property>]
    let ``annualizeReturn is monotonic in daily return`` (a: NormalFloat) (b: NormalFloat) =
        let da = (abs a.Get % 0.05)
        let db = (abs b.Get % 0.05)
        (da <= db) ==> lazy (
            Returns.annualizeReturn 252.0 da <= Returns.annualizeReturn 252.0 db + 1e-9)
