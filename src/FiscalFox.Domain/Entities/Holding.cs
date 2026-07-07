namespace FiscalFox.Domain.Entities;

/// <summary>
/// A position: how many units of an <see cref="Instrument"/> an <see cref="Account"/>
/// currently holds, plus the average cost basis used for P/L.
/// </summary>
public class Holding
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public int InstrumentId { get; set; }
    public Instrument? Instrument { get; set; }

    /// <summary>Number of units/shares currently held. May be fractional.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Average purchase price per unit in the account currency.</summary>
    public decimal AverageCost { get; set; }

    /// <summary>Optional user-set target weight (0..1) for rebalancing.</summary>
    public decimal? TargetWeight { get; set; }

    /// <summary>
    /// Cumulative realized profit/loss booked on Sell transactions
    /// (proceeds minus average cost of units sold, net of fees).
    /// Unrealized P/L is computed on the fly from the live price.
    /// </summary>
    public decimal RealizedPnL { get; set; }
}
