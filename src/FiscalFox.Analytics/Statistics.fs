namespace FiscalFox.Analytics

/// Small, dependency-free numerical helpers used across the analytics engine.
/// Everything here is a pure function so it is trivial to property-test.
module Statistics =

    /// Arithmetic mean. Returns 0.0 for an empty sequence.
    let mean (xs: float list) : float =
        match xs with
        | [] -> 0.0
        | _ -> List.sum xs / float (List.length xs)

    /// Sample variance (n-1 denominator). Returns 0.0 when fewer than 2 points.
    let variance (xs: float list) : float =
        match xs with
        | [] | [ _ ] -> 0.0
        | _ ->
            let m = mean xs
            let ss = xs |> List.sumBy (fun x -> (x - m) * (x - m))
            ss / float (List.length xs - 1)

    /// Sample standard deviation (n-1 denominator).
    let stdDev (xs: float list) : float =
        sqrt (variance xs)

    /// Population covariance of two equal-length series.
    /// Uses the shorter length if the inputs differ so it never throws.
    let covariance (xs: float list) (ys: float list) : float =
        let n = min (List.length xs) (List.length ys)
        if n < 2 then
            0.0
        else
            let xs' = xs |> List.truncate n
            let ys' = ys |> List.truncate n
            let mx = mean xs'
            let my = mean ys'
            let acc =
                List.zip xs' ys'
                |> List.sumBy (fun (x, y) -> (x - mx) * (y - my))
            acc / float (n - 1)

    /// Pearson correlation coefficient in [-1, 1]. Returns 0.0 for degenerate input.
    /// Both series are aligned to their common length so covariance and the two
    /// standard deviations are computed over the exact same observations —
    /// otherwise the ratio could fall outside [-1, 1] for mismatched inputs.
    let correlation (xs: float list) (ys: float list) : float =
        let n = min (List.length xs) (List.length ys)
        if n < 2 then
            0.0
        else
            let xs' = xs |> List.truncate n
            let ys' = ys |> List.truncate n
            let sx = stdDev xs'
            let sy = stdDev ys'
            if sx = 0.0 || sy = 0.0 then
                0.0
            else
                covariance xs' ys' / (sx * sy)

    /// Linear-interpolated percentile of a sample. p is a fraction in [0, 1].
    /// Mirrors the common "type 7" quantile used by NumPy/Excel PERCENTILE.INC.
    let percentile (p: float) (xs: float list) : float =
        match xs with
        | [] -> 0.0
        | _ ->
            let sorted = List.sort xs
            let n = List.length sorted
            if n = 1 then
                List.head sorted
            else
                let pClamped = max 0.0 (min 1.0 p)
                let rank = pClamped * float (n - 1)
                let lo = int (floor rank)
                let hi = int (ceil rank)
                let frac = rank - float lo
                let vLo = sorted.[lo]
                let vHi = sorted.[hi]
                vLo + frac * (vHi - vLo)
