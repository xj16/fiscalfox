namespace FiscalFox.Domain.Entities;

/// <summary>
/// A single daily OHLC price bar, cached from open data (Stooq / Yahoo CSV export).
/// The analytics engine consumes closing prices to compute returns and risk.
/// </summary>
public class PriceBar
{
    public long Id { get; set; }

    public int InstrumentId { get; set; }
    public Instrument? Instrument { get; set; }

    public DateOnly Date { get; set; }

    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
}
