using FiscalFox.Api.Data;
using FiscalFox.Api.Dtos;
using FiscalFox.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FiscalFox.Api.Services;

/// <summary>
/// Outcome of applying a transaction: either the persisted transaction (mapped
/// to a DTO) or a validation error the controller turns into a 400/404.
/// </summary>
public sealed record TransactionResult(TransactionDto? Transaction, string? Error, bool NotFound = false)
{
    public static TransactionResult Ok(TransactionDto dto) => new(dto, null);
    public static TransactionResult Bad(string error) => new(null, error);
    public static TransactionResult Missing(string error) => new(null, error, NotFound: true);
}

/// <summary>
/// Applies account transactions and keeps holdings, cash balance and cost basis
/// consistent. This is the engine that makes the modeled <see cref="Transaction"/>
/// real: Buy/Sell update the holding's quantity + moving-average cost, Sell books
/// realized P/L, and every kind adjusts <see cref="Account.CashBalance"/>.
///
/// Cost-basis convention: a Buy folds its fee into the average cost (so the fee
/// capitalizes into the position); a Sell expenses its fee against realized P/L.
/// This mirrors how most brokerage cost-basis reports treat commissions.
/// </summary>
public class TransactionService
{
    private readonly FiscalFoxDbContext _db;

    public TransactionService(FiscalFoxDbContext db) => _db = db;

    /// <summary>List an account's transactions, newest first.</summary>
    public async Task<IReadOnlyList<TransactionDto>?> ListAsync(int accountId, CancellationToken ct = default)
    {
        if (!await _db.Accounts.AnyAsync(a => a.Id == accountId, ct))
            return null;

        return await _db.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.TimestampUtc).ThenByDescending(t => t.Id)
            .Include(t => t.Instrument)
            .Select(t => new TransactionDto(
                t.Id, t.Kind, t.Instrument != null ? t.Instrument.Symbol : null,
                t.Quantity, t.Price, t.CashImpact, t.Fee, t.TimestampUtc, t.Note))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Validate and apply a transaction, mutating the holding and cash balance in
    /// a single unit of work. Returns a structured result for the controller.
    /// </summary>
    public async Task<TransactionResult> ApplyAsync(int accountId, CreateTransactionDto dto, CancellationToken ct = default)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
            return TransactionResult.Missing("Account not found.");

        if (dto.Fee < 0)
            return TransactionResult.Bad("Fee cannot be negative.");

        var timestamp = DateTime.UtcNow;
        var tx = new Transaction
        {
            AccountId = accountId,
            Kind = dto.Kind,
            Quantity = dto.Quantity,
            Price = dto.Price,
            Fee = dto.Fee,
            Note = dto.Note,
            TimestampUtc = timestamp
        };

        string? error = dto.Kind switch
        {
            TransactionKind.Buy => await ApplyBuyAsync(account, dto, tx, ct),
            TransactionKind.Sell => await ApplySellAsync(account, dto, tx, ct),
            TransactionKind.Dividend => await ApplyDividendAsync(account, dto, tx, ct),
            TransactionKind.Deposit => ApplyDeposit(account, dto, tx),
            TransactionKind.Withdrawal => ApplyWithdrawal(account, dto, tx),
            TransactionKind.Fee => ApplyFee(account, dto, tx),
            _ => "Unsupported transaction kind."
        };

        if (error is not null)
            return TransactionResult.Bad(error);

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync(ct);

        string? symbol = null;
        if (tx.InstrumentId is int iid)
            symbol = await _db.Instruments.Where(i => i.Id == iid).Select(i => i.Symbol).FirstOrDefaultAsync(ct);

        return TransactionResult.Ok(new TransactionDto(
            tx.Id, tx.Kind, symbol, tx.Quantity, tx.Price, tx.CashImpact, tx.Fee, tx.TimestampUtc, tx.Note));
    }

    // ---- Trade kinds -------------------------------------------------------

    private async Task<string?> ApplyBuyAsync(Account account, CreateTransactionDto dto, Transaction tx, CancellationToken ct)
    {
        if (dto.Quantity <= 0) return "Buy quantity must be positive.";
        if (dto.Price < 0) return "Buy price cannot be negative.";

        var (instrument, err) = await ResolveInstrumentAsync(dto.Symbol, ct);
        if (err is not null) return err;

        var holding = await GetOrCreateHoldingAsync(account.Id, instrument!.Id, ct);

        var cost = dto.Quantity * dto.Price + dto.Fee; // fee capitalizes into basis
        var newQty = holding.Quantity + dto.Quantity;
        // Moving-average cost basis, fee included.
        holding.AverageCost = newQty == 0 ? 0 : (holding.Quantity * holding.AverageCost + cost) / newQty;
        holding.Quantity = newQty;

        tx.InstrumentId = instrument.Id;
        tx.CashImpact = -cost;
        account.CashBalance += tx.CashImpact;
        return null;
    }

    private async Task<string?> ApplySellAsync(Account account, CreateTransactionDto dto, Transaction tx, CancellationToken ct)
    {
        if (dto.Quantity <= 0) return "Sell quantity must be positive.";
        if (dto.Price < 0) return "Sell price cannot be negative.";

        var (instrument, err) = await ResolveInstrumentAsync(dto.Symbol, ct);
        if (err is not null) return err;

        var holding = await _db.Holdings
            .FirstOrDefaultAsync(h => h.AccountId == account.Id && h.InstrumentId == instrument!.Id, ct);
        if (holding is null || holding.Quantity <= 0)
            return $"No position in '{instrument!.Symbol}' to sell.";
        if (dto.Quantity > holding.Quantity)
            return $"Cannot sell {dto.Quantity} units; only {holding.Quantity} held.";

        var proceeds = dto.Quantity * dto.Price - dto.Fee; // fee expensed against proceeds
        // Realized P/L against average cost of the units sold.
        holding.RealizedPnL += dto.Quantity * (dto.Price - holding.AverageCost) - dto.Fee;
        holding.Quantity -= dto.Quantity;
        if (holding.Quantity == 0)
            holding.AverageCost = 0; // flat position resets basis

        tx.InstrumentId = instrument!.Id;
        tx.CashImpact = proceeds;
        account.CashBalance += proceeds;
        return null;
    }

    private async Task<string?> ApplyDividendAsync(Account account, CreateTransactionDto dto, Transaction tx, CancellationToken ct)
    {
        // Dividend cash = Quantity (shares) * Price (per-share dividend), or if
        // Quantity is 0 the caller may pass the gross amount in Price directly.
        var gross = dto.Quantity > 0 ? dto.Quantity * dto.Price : dto.Price;
        if (gross < 0) return "Dividend amount cannot be negative.";

        int? instrumentId = null;
        if (!string.IsNullOrWhiteSpace(dto.Symbol))
        {
            var (instrument, err) = await ResolveInstrumentAsync(dto.Symbol, ct);
            if (err is not null) return err;
            instrumentId = instrument!.Id;
        }

        tx.InstrumentId = instrumentId;
        tx.CashImpact = gross - dto.Fee;
        account.CashBalance += tx.CashImpact;
        return null;
    }

    // ---- Cash kinds --------------------------------------------------------

    private static string? ApplyDeposit(Account account, CreateTransactionDto dto, Transaction tx)
    {
        var amount = CashAmount(dto);
        if (amount <= 0) return "Deposit amount must be positive.";
        tx.CashImpact = amount - dto.Fee;
        account.CashBalance += tx.CashImpact;
        return null;
    }

    private static string? ApplyWithdrawal(Account account, CreateTransactionDto dto, Transaction tx)
    {
        var amount = CashAmount(dto);
        if (amount <= 0) return "Withdrawal amount must be positive.";
        var total = amount + dto.Fee;
        if (total > account.CashBalance)
            return $"Insufficient cash: withdrawal of {total} exceeds balance {account.CashBalance}.";
        tx.CashImpact = -total;
        account.CashBalance += tx.CashImpact;
        return null;
    }

    private static string? ApplyFee(Account account, CreateTransactionDto dto, Transaction tx)
    {
        if (dto.Fee <= 0) return "Fee amount must be positive.";
        tx.CashImpact = -dto.Fee;
        account.CashBalance += tx.CashImpact;
        return null;
    }

    // ---- Helpers -----------------------------------------------------------

    /// <summary>Cash movement size for deposit/withdrawal: prefer Price, fall back to Quantity.</summary>
    private static decimal CashAmount(CreateTransactionDto dto) => dto.Price != 0 ? dto.Price : dto.Quantity;

    private async Task<(Instrument? Instrument, string? Error)> ResolveInstrumentAsync(string? symbol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return (null, "Symbol is required for this transaction kind.");
        var instrument = await _db.Instruments.FirstOrDefaultAsync(i => i.Symbol == symbol, ct);
        if (instrument is null)
            return (null, $"Unknown instrument symbol '{symbol}'.");
        return (instrument, null);
    }

    private async Task<Holding> GetOrCreateHoldingAsync(int accountId, int instrumentId, CancellationToken ct)
    {
        var holding = await _db.Holdings
            .FirstOrDefaultAsync(h => h.AccountId == accountId && h.InstrumentId == instrumentId, ct);
        if (holding is null)
        {
            holding = new Holding { AccountId = accountId, InstrumentId = instrumentId, Quantity = 0, AverageCost = 0 };
            _db.Holdings.Add(holding);
        }
        return holding;
    }
}
