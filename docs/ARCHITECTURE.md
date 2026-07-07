# FiscalFox architecture

FiscalFox is a small **polyglot .NET** application. The selling point is the
split between an imperative C# web/persistence layer and a **pure F# analytics
engine** — so this doc focuses on that boundary and on the exact conventions
each metric uses.

```
┌────────────────────┐     HTTP/JSON      ┌────────────────────┐
│  FiscalFox.Web      │ ─────────────────▶ │  FiscalFox.Api      │
│  Blazor Server UI   │ ◀───────────────── │  ASP.NET Core       │
│  inline-SVG charts  │                    │  controllers + EF   │
└────────────────────┘                    └─────────┬──────────┘
                                                     │ materialize
                                                     │ prices → float list
                                                     ▼
                                          ┌────────────────────┐
                                          │ FiscalFox.Analytics │
                                          │ pure F# functions   │
                                          │ (no EF, no I/O)     │
                                          └─────────┬──────────┘
                                                     │ shared DTOs
                                                     ▼
                                          ┌────────────────────┐
                                          │ FiscalFox.Domain    │
                                          │ C# entities + DTOs  │
                                          └────────────────────┘
```

## Projects

| Project | Language | Responsibility |
| --- | --- | --- |
| `FiscalFox.Domain` | C# | EF entities (`Account`, `Instrument`, `Holding`, `PriceBar`, `Transaction`) and the analytics **DTOs** the F# engine returns (`PortfolioReport`, `RiskReturnStats`, `CorrelationMatrix`, `EfficientFrontier`, …). Both C# and F# reference this, so the record types are the shared vocabulary. |
| `FiscalFox.Analytics` | **F#** | Every quantitative function — `Statistics`, `Returns`, `Risk`, `Rebalancing`, `PortfolioMath` (correlation + frontier), `Portfolio` (the façade). Pure: it takes `float list`s and returns DTOs. No EF, no config, no clock. |
| `FiscalFox.Api` | C# | Controllers, EF Core (MySQL via Pomelo, or in-memory), seeding, the transaction engine, and the glue that turns EF rows into F# inputs. |
| `FiscalFox.Web` | C# | Blazor Server dashboard. Talks to the API over a typed `HttpClient`; renders KPIs, tables, and the inline-SVG charts. |

## The C# ↔ F# boundary

The API never does math. `AnalyticsService` is the entire bridge: it loads rows
with EF, shapes them into the F# input type, calls one F# function, and returns
the DTO the function produced. A few interop details are worth knowing:

- **Materialize before crossing.** EF `IQueryable`s are turned into concrete
  `List<double>` (closing prices) *before* they reach F#. The engine must stay
  free of `IQueryable`/expression-tree concerns; it only ever sees data.
- **`float list` in, arrays out.** F# functions consume `float list`
  (`ListModule.OfSeq(...)` on the C# side). The DTOs expose `IReadOnlyList<T>`;
  since an F# `list` does **not** implement `IReadOnlyList<T>` but an array
  does, `Portfolio.report` materializes its results with `List.toArray` before
  handing them back.
- **Options map to nullables.** An optional target weight is
  `FSharpOption<double>` in F# and surfaces as `double?` in the DTO
  (`Option.toNullable`). On the way in, a C# `decimal?` becomes
  `FSharpOption<double>.Some/None`.
- **`decimal` vs `double`.** Money and quantities are `decimal` in the EF layer
  (exact), but the analytics work in `double` (fast, and correct for
  statistics). The cast happens exactly once, at the boundary.
- **No N+1.** `AnalyticsService.LoadClosesAsync` pulls every needed instrument's
  closes in a single grouped query and hands the engine a per-instrument
  dictionary, rather than querying per holding.

## Transaction engine (cost basis)

`TransactionService` is the one place holdings mutate. Conventions:

- **Buy** — `quantity += q`; average cost recomputed as a **moving average with
  the fee capitalized**: `avg' = (oldQty·oldAvg + q·price + fee) / (oldQty + q)`.
  Cash `-= q·price + fee`.
- **Sell** — realized P/L `+= q·(price − avg) − fee` (fee **expensed**);
  `quantity -= q`; the average cost is unchanged (and resets to 0 when the
  position goes flat). Cash `+= q·price − fee`. Over-selling is rejected.
- **Dividend** — cash `+= q·perShare − fee` (or a flat amount if `q = 0`).
- **Deposit / Withdrawal / Fee** — cash-only; withdrawals guard against
  overdraft.

Unrealized P/L is never stored — it is computed on read as
`(lastClose − avgCost) · quantity`.

## Analytics module map & formula reference

All series are **daily**; annualization assumes `252` trading days. Variance and
standard deviation use the **sample** (n−1) denominator.

| Module | Function | Definition / convention |
| --- | --- | --- |
| `Statistics` | `mean`, `variance`, `stdDev` | arithmetic mean; sample variance (n−1); `stdDev = √variance`. |
| `Statistics` | `covariance`, `correlation` | population-style covariance over the common length; Pearson `ρ = cov(x,y)/(σx·σy)`, clamped-safe (returns 0 for degenerate input), always in `[-1, 1]`. |
| `Statistics` | `percentile p` | linear-interpolated "type 7" quantile (NumPy/Excel `PERCENTILE.INC`). |
| `Returns` | `simpleReturns` | `rₜ = (pₜ − pₜ₋₁)/pₜ₋₁`; `n` prices → `n−1` returns. |
| `Returns` | `logReturns` | `ln(pₜ/pₜ₋₁)`. |
| `Returns` | `cumulativeIndex` | wealth index base `1.0`, compounding simple returns — the equity curve. |
| `Returns` | `annualizeReturn` | `(1 + r̄)²⁵² − 1` on the mean daily return. |
| `Risk` | `annualizeVolatility` | `σ_daily · √252`. |
| `Risk` | `maxDrawdown` | max peak-to-trough decline of the equity curve, as a **positive** fraction in `[0, 1]`; `0` for a monotonically rising curve. |
| `Risk` | `historicalVaR c` | `max(0, −percentile(1−c))` of the return series — the loss threshold on the worst `(1−c)` of days, positive. |
| `Risk` | `sharpeRatio` | `(annualReturn − annualRiskFree) / annualVol`; `0` when volatility is `0`. |
| `Rebalancing` | `plan minTrade` | normalize targets → per-symbol `Δ = targetValue − marketValue`; emit Buy/Sell trades whose `|Δ|` exceeds `minTrade`. |
| `PortfolioMath` | `correlationMatrix` | tail-aligns each asset's returns to their common length, then fills a symmetric matrix with a unit diagonal. |
| `PortfolioMath` | `efficientFrontier` | draws `samples` random long-only weight vectors (normalized exponentials — a Dirichlet-ish cloud) plus equal-weight and single-name anchors; scores each on the annualized risk/return plane; reports the max-Sharpe portfolio and (optionally) the current allocation. Deterministic for a fixed seed; non-finite scores are coerced to `0`. |
| `Portfolio` | `positionValues`, `report`, `timeseries` | the façade: values holdings, value-weights their returns (tail-aligned so uneven histories never throw), and assembles the `PortfolioReport` / `PortfolioTimeseries`. |

## Why this shape

- **The math is a library, not a service.** Because every analytic is a pure
  function of `float list`s, the whole engine is exhaustively **property-tested**
  with FsCheck (weights sum to 1, VaR ≥ 0, correlation ∈ [−1, 1], …) rather than
  only spot-checked.
- **The API is thin.** Controllers validate, load, delegate, and serialize.
  Business rules that involve money (the transaction engine) live in one service
  with explicit guards.
- **The UI has no dependencies.** Charts are hand-rolled inline SVG, so the whole
  stack is self-hostable with no CDN and nothing phones home.
