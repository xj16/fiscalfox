# Changelog

All notable changes to FiscalFox are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] — 2026-07-07

The "make the pitch true" release: the modeled-but-dormant transaction domain is
now real and drives holdings, the F# engine's cross-asset math is exposed, and
the dashboard finally *shows* the analytics instead of only tabulating them.

### Added

- **Transaction engine.** `TransactionService` + `TransactionsController`
  (`GET`/`POST /api/accounts/{id}/transactions`) apply
  Buy / Sell / Dividend / Deposit / Withdrawal / Fee:
  - Buys fold their fee into a **moving-average cost basis**; sells book
    **realized P/L** against that basis and reset it when the position goes flat.
  - Every kind adjusts the account **cash balance**, with over-sell and
    over-draft guards.
  - Holdings now expose `RealizedPnL` and a live **unrealized P/L**.
- **Cross-asset analytics** built on the (already property-tested)
  `Statistics.covariance`/`correlation` primitives:
  - `GET /api/analytics/correlation` — symbol-by-symbol Pearson **correlation
    matrix** over tail-aligned daily returns.
  - `GET /api/analytics/frontier/{accountId}` — a **Markowitz efficient-frontier**
    sampler (random long-only portfolios + the max-Sharpe/tangency portfolio +
    the account's current allocation on the same axes).
  - `GET /api/analytics/portfolio/{accountId}/timeseries` — the value-weighted
    **equity curve + drawdown series** (surfacing the F# `cumulativeIndex`).
- **Dependency-free inline-SVG charts** on the Blazor dashboard — equity/drawdown
  line, allocation donut, correlation heatmap, and efficient-frontier scatter.
  No CDN, no JS charting lib, fully self-hostable and server-rendered.
- **Security hardening for exposed deployments**: per-IP fixed-window **rate
  limiting**, a **locked-down CORS** policy (configured origins instead of
  any-origin), and an optional **API-key** middleware
  (`FiscalFox:ApiKey`) — all off/permissive by default so the demo stays open.
- **Coverage** collection (`coverlet`) wired into CI with a ReportGenerator
  summary and an uploaded HTML report artifact.
- Flagship tests: 15 new F# properties — for the correlation matrix and frontier
  (symmetry, unit diagonal, bounded entries, long-only weights, max-Sharpe
  dominance, seed determinism) and for the value-weighted `Portfolio.report`
  aggregation (weights sum to 1, order-invariance of value and risk) — plus 8 new
  API integration tests covering the transaction lifecycle and the new analytics
  endpoints. **35 → 50** F# tests, **8 → 16** API tests (66 total).
- Architecture guide (`docs/ARCHITECTURE.md`) covering the C#↔F# boundary and a
  formula reference for every metric.
- A `demo` docker-compose profile plus a richer seed that builds the demo
  portfolio **through the transaction engine** (deposit → buys → a partial sell
  that realizes a gain → a dividend) so it comes up with an honest history.

### Changed

- The demo portfolio is now seeded via real transactions rather than by writing
  holding rows directly, giving it a genuine cost basis and realized P/L.
- Holdings list and analytics no longer issue a "last close" query per row —
  latest closes are loaded in a single grouped query (**N+1 removed**).

### Fixed

- The efficient-frontier evaluator now coerces non-finite (NaN/Infinity) risk,
  return and Sharpe values to `0.0`, so a pathological price series can never
  poison a max/min selection or a comparison. (Surfaced by the new determinism
  property test.)

## [0.1.0] — 2026 (initial public release)

- ASP.NET Core (C#) Web API with a pure **F#** analytics engine
  (returns, risk, rebalancing), EF Core over MySQL (Pomelo) with an in-memory
  option, and a Blazor Server dashboard.
- FsCheck property tests over the analytics engine; xUnit API integration tests;
  GitHub Actions CI with a real MySQL end-to-end smoke test.
- Cached, regenerable open-data price CSVs (no paid market-data API).

[0.2.0]: https://github.com/xj16/fiscalfox/releases/tag/v0.2.0
[0.1.0]: https://github.com/xj16/fiscalfox/releases/tag/v0.1.0
