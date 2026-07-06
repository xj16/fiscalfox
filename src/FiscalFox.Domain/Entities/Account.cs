namespace FiscalFox.Domain.Entities;

/// <summary>
/// A financial account the user tracks (brokerage, bank, wallet, ...).
/// Holdings and cash transactions roll up under an account.
/// </summary>
public class Account
{
    public int Id { get; set; }

    /// <summary>Human-readable name, e.g. "Main Brokerage".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ISO-4217 base currency of the account, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    public AccountType Type { get; set; } = AccountType.Brokerage;

    /// <summary>Free cash balance held in the account, in <see cref="Currency"/>.</summary>
    public decimal CashBalance { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<Holding> Holdings { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
}

public enum AccountType
{
    Brokerage = 0,
    Bank = 1,
    Retirement = 2,
    Crypto = 3,
    Cash = 4
}
