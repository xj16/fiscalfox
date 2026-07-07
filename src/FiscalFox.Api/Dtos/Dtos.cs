using FiscalFox.Domain.Entities;

namespace FiscalFox.Api.Dtos;

public record AccountDto(int Id, string Name, string Currency, AccountType Type, decimal CashBalance);

public record CreateAccountDto(string Name, string Currency, AccountType Type, decimal CashBalance);

public record InstrumentDto(int Id, string Symbol, string Name, AssetClass AssetClass, string Currency, decimal? LastClose);

public record HoldingDto(
    int Id, int InstrumentId, string Symbol, decimal Quantity,
    decimal AverageCost, decimal? TargetWeight, decimal LastClose, decimal MarketValue,
    decimal RealizedPnL, decimal UnrealizedPnL);

public record CreateHoldingDto(string Symbol, decimal Quantity, decimal AverageCost, decimal? TargetWeight);

public record TransactionDto(
    long Id, TransactionKind Kind, string? Symbol, decimal Quantity,
    decimal Price, decimal CashImpact, decimal Fee, DateTime TimestampUtc, string? Note);

public record CreateTransactionDto(
    TransactionKind Kind, string? Symbol, decimal Quantity, decimal Price, decimal Fee, string? Note);
