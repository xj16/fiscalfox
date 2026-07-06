namespace FiscalFox.Domain.Entities;

/// <summary>
/// A cash or trade movement in an account. Buys/Sells adjust holdings and cash;
/// Deposits/Withdrawals/Dividends adjust cash only.
/// </summary>
public class Transaction
{
    public long Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>Nullable: cash-only transactions (deposit, withdrawal) have no instrument.</summary>
    public int? InstrumentId { get; set; }
    public Instrument? Instrument { get; set; }

    public TransactionKind Kind { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Units traded (0 for pure cash movements).</summary>
    public decimal Quantity { get; set; }

    /// <summary>Price per unit for trades; unused for cash movements.</summary>
    public decimal Price { get; set; }

    /// <summary>Signed cash impact in the account currency (negative = cash out).</summary>
    public decimal CashImpact { get; set; }

    /// <summary>Broker fees / commissions applied to this transaction.</summary>
    public decimal Fee { get; set; }

    public string? Note { get; set; }
}

public enum TransactionKind
{
    Buy = 0,
    Sell = 1,
    Deposit = 2,
    Withdrawal = 3,
    Dividend = 4,
    Fee = 5
}
