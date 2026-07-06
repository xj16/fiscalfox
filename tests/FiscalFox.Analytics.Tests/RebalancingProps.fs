namespace FiscalFox.Analytics.Tests

open FsCheck
open FsCheck.Xunit
open FiscalFox.Analytics

module RebalancingProps =

    /// Build a small set of positions with random positive quantities/prices
    /// and normalized target weights.
    let private positionGen : Gen<Rebalancing.PositionInput list> =
        gen {
            let! n = Gen.choose (2, 6)
            let! qtys = Gen.listOfLength n (Gen.choose (1, 1000) |> Gen.map float)
            let! prices = Gen.listOfLength n (Gen.choose (1, 50000) |> Gen.map (fun c -> float c / 100.0))
            let! targets = Gen.listOfLength n (Gen.choose (0, 100) |> Gen.map float)
            let symbols = [ for i in 0 .. n - 1 -> sprintf "SYM%d" i ]
            return
                List.map3
                    (fun s (q, p) t ->
                        { Rebalancing.PositionInput.Symbol = s
                          Rebalancing.PositionInput.Quantity = q
                          Rebalancing.PositionInput.Price = p
                          Rebalancing.PositionInput.TargetWeight = Some t })
                    symbols
                    (List.zip qtys prices)
                    targets
        }

    type RebalArb =
        static member Positions() : Arbitrary<Rebalancing.PositionInput list> =
            Arb.fromGen positionGen

    [<Property(Arbitrary = [| typeof<RebalArb> |])>]
    let ``every action has positive units and amount`` (positions: Rebalancing.PositionInput list) =
        Rebalancing.plan 0.0 positions
        |> List.forall (fun a -> a.Units > 0.0 && a.Amount > 0.0)

    [<Property(Arbitrary = [| typeof<RebalArb> |])>]
    let ``side matches direction of weight change`` (positions: Rebalancing.PositionInput list) =
        Rebalancing.plan 0.0 positions
        |> List.forall (fun a ->
            if a.Side = "Buy" then a.TargetWeight >= a.CurrentWeight - 1e-9
            else a.TargetWeight <= a.CurrentWeight + 1e-9)

    [<Property(Arbitrary = [| typeof<RebalArb> |])>]
    let ``a higher minTrade never produces more trades`` (positions: Rebalancing.PositionInput list) =
        let few = Rebalancing.plan 1000.0 positions |> List.length
        let many = Rebalancing.plan 0.0 positions |> List.length
        few <= many

    [<Property(Arbitrary = [| typeof<RebalArb> |])>]
    let ``turnover is non-negative`` (positions: Rebalancing.PositionInput list) =
        Rebalancing.plan 0.0 positions |> Rebalancing.turnover >= 0.0

    [<Property(Arbitrary = [| typeof<RebalArb> |])>]
    let ``already-on-target portfolio needs no trades`` (positions: Rebalancing.PositionInput list) =
        // Set each target weight equal to its current weight -> no rebalancing.
        let total = positions |> List.sumBy (fun p -> p.Quantity * p.Price)
        (total > 0.0) ==> lazy (
            let onTarget =
                positions
                |> List.map (fun p ->
                    let w = (p.Quantity * p.Price) / total
                    { p with Rebalancing.PositionInput.TargetWeight = Some w })
            Rebalancing.plan 0.01 onTarget |> List.isEmpty)
