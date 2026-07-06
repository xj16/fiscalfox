namespace FiscalFox.Analytics.Tests

open Xunit
open FiscalFox.Analytics

/// Concrete, hand-computed examples that pin down the analytics engine's output.
module PortfolioTests =

    let private approx (tol: float) (expected: float) (actual: float) =
        Assert.True(abs (expected - actual) < tol,
            sprintf "expected %f, got %f (tol %f)" expected actual tol)

    [<Fact>]
    let ``simpleReturns computes textbook values`` () =
        let rets = Returns.simpleReturns [ 100.0; 110.0; 99.0 ]
        Assert.Equal(2, List.length rets)
        approx 1e-9 0.10 rets.[0]
        approx 1e-9 -0.10 rets.[1]

    [<Fact>]
    let ``totalReturn is last over first minus one`` () =
        approx 1e-9 0.25 (Returns.totalReturn [ 80.0; 90.0; 100.0 ])

    [<Fact>]
    let ``maxDrawdown of a known curve`` () =
        // Peak 120 -> trough 90 == 25% drawdown.
        let curve = [ 100.0; 120.0; 90.0; 110.0 ]
        approx 1e-9 0.25 (Risk.maxDrawdown curve)

    [<Fact>]
    let ``mean of a known sample`` () =
        approx 1e-9 3.0 (Statistics.mean [ 1.0; 2.0; 3.0; 4.0; 5.0 ])

    [<Fact>]
    let ``percentile median matches middle value`` () =
        approx 1e-9 3.0 (Statistics.percentile 0.5 [ 1.0; 2.0; 3.0; 4.0; 5.0 ])

    [<Fact>]
    let ``report values a two-asset portfolio and weights it`` () =
        let holdings =
            [ { Portfolio.HoldingInput.Symbol = "AAA"
                Portfolio.HoldingInput.Quantity = 10.0
                Portfolio.HoldingInput.Prices = [ 90.0; 100.0 ]   // last price 100 -> value 1000
                Portfolio.HoldingInput.TargetWeight = Some 0.5 }
              { Portfolio.HoldingInput.Symbol = "BBB"
                Portfolio.HoldingInput.Quantity = 20.0
                Portfolio.HoldingInput.Prices = [ 45.0; 50.0 ]    // last price 50 -> value 1000
                Portfolio.HoldingInput.TargetWeight = Some 0.5 } ]

        let report = Portfolio.report 0.04 10.0 holdings

        approx 1e-6 2000.0 report.TotalValue
        Assert.Equal(2, report.Positions.Count)
        // Equal values -> 50/50 weights, already on target -> no rebalancing.
        for p in report.Positions do
            approx 1e-6 0.5 p.Weight
        Assert.Empty(report.Rebalance)

    [<Fact>]
    let ``report suggests a rebalance when weights drift`` () =
        let holdings =
            [ { Portfolio.HoldingInput.Symbol = "AAA"
                Portfolio.HoldingInput.Quantity = 30.0
                Portfolio.HoldingInput.Prices = [ 100.0 ]   // value 3000 (75%)
                Portfolio.HoldingInput.TargetWeight = Some 0.5 }
              { Portfolio.HoldingInput.Symbol = "BBB"
                Portfolio.HoldingInput.Quantity = 10.0
                Portfolio.HoldingInput.Prices = [ 100.0 ]   // value 1000 (25%)
                Portfolio.HoldingInput.TargetWeight = Some 0.5 } ]

        let report = Portfolio.report 0.04 10.0 holdings
        approx 1e-6 4000.0 report.TotalValue
        Assert.NotEmpty(report.Rebalance)
        // AAA is overweight -> should sell; BBB underweight -> should buy.
        let aaa = report.Rebalance |> Seq.find (fun a -> a.Symbol = "AAA")
        let bbb = report.Rebalance |> Seq.find (fun a -> a.Symbol = "BBB")
        Assert.Equal("Sell", aaa.Side)
        Assert.Equal("Buy", bbb.Side)
        // Each should move 1000 to reach 2000/2000.
        approx 1e-6 1000.0 aaa.Amount
        approx 1e-6 1000.0 bbb.Amount
