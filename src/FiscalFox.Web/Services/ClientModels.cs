namespace FiscalFox.Web.Services;

// Lightweight client-side mirrors of the API response shapes. Kept here (rather
// than referencing the API project) so the Web app depends only on Domain.

public record AccountVm(int Id, string Name, string Currency, int Type, decimal CashBalance);

public record InstrumentVm(int Id, string Symbol, string Name, int AssetClass, string Currency, decimal? LastClose);

public record HoldingVm(
    int Id, int InstrumentId, string Symbol, decimal Quantity,
    decimal AverageCost, decimal? TargetWeight, decimal LastClose, decimal MarketValue);

public record PricePointVm(DateOnly Date, decimal Close);
