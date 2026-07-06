using FiscalFox.Api.Data;
using FiscalFox.Api.Dtos;
using FiscalFox.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiscalFox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    private readonly FiscalFoxDbContext _db;

    public AccountsController(FiscalFoxDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AccountDto>>> GetAll(CancellationToken ct)
    {
        var accounts = await _db.Accounts
            .OrderBy(a => a.Id)
            .Select(a => new AccountDto(a.Id, a.Name, a.Currency, a.Type, a.CashBalance))
            .ToListAsync(ct);
        return Ok(accounts);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AccountDto>> Get(int id, CancellationToken ct)
    {
        var a = await _db.Accounts.FindAsync(new object[] { id }, ct);
        if (a is null)
            return NotFound();
        return new AccountDto(a.Id, a.Name, a.Currency, a.Type, a.CashBalance);
    }

    [HttpPost]
    public async Task<ActionResult<AccountDto>> Create(CreateAccountDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name is required.");

        var account = new Account
        {
            Name = dto.Name.Trim(),
            Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency.ToUpperInvariant(),
            Type = dto.Type,
            CashBalance = dto.CashBalance
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(ct);

        var result = new AccountDto(account.Id, account.Name, account.Currency, account.Type, account.CashBalance);
        return CreatedAtAction(nameof(Get), new { id = account.Id }, result);
    }

    [HttpGet("{id:int}/holdings")]
    public async Task<ActionResult<IEnumerable<HoldingDto>>> GetHoldings(int id, CancellationToken ct)
    {
        var exists = await _db.Accounts.AnyAsync(a => a.Id == id, ct);
        if (!exists)
            return NotFound();

        var holdings = await _db.Holdings
            .Where(h => h.AccountId == id)
            .Include(h => h.Instrument)
            .ToListAsync(ct);

        var result = new List<HoldingDto>();
        foreach (var h in holdings)
        {
            var lastClose = await _db.PriceBars
                .Where(p => p.InstrumentId == h.InstrumentId)
                .OrderByDescending(p => p.Date)
                .Select(p => p.Close)
                .FirstOrDefaultAsync(ct);

            result.Add(new HoldingDto(
                h.Id, h.InstrumentId, h.Instrument!.Symbol, h.Quantity,
                h.AverageCost, h.TargetWeight, lastClose, h.Quantity * lastClose));
        }
        return Ok(result);
    }

    [HttpPost("{id:int}/holdings")]
    public async Task<ActionResult<HoldingDto>> AddHolding(int id, CreateHoldingDto dto, CancellationToken ct)
    {
        var account = await _db.Accounts.FindAsync(new object[] { id }, ct);
        if (account is null)
            return NotFound("Account not found.");

        var instrument = await _db.Instruments
            .FirstOrDefaultAsync(i => i.Symbol == dto.Symbol, ct);
        if (instrument is null)
            return BadRequest($"Unknown instrument symbol '{dto.Symbol}'.");

        if (dto.Quantity <= 0)
            return BadRequest("Quantity must be positive.");

        if (dto.TargetWeight is < 0 or > 1)
            return BadRequest("TargetWeight must be between 0 and 1.");

        var existing = await _db.Holdings
            .FirstOrDefaultAsync(h => h.AccountId == id && h.InstrumentId == instrument.Id, ct);

        if (existing is not null)
        {
            existing.Quantity += dto.Quantity;
            existing.AverageCost = dto.AverageCost;
            existing.TargetWeight = dto.TargetWeight ?? existing.TargetWeight;
        }
        else
        {
            existing = new Holding
            {
                AccountId = id,
                InstrumentId = instrument.Id,
                Quantity = dto.Quantity,
                AverageCost = dto.AverageCost,
                TargetWeight = dto.TargetWeight
            };
            _db.Holdings.Add(existing);
        }

        await _db.SaveChangesAsync(ct);

        var lastClose = await _db.PriceBars
            .Where(p => p.InstrumentId == instrument.Id)
            .OrderByDescending(p => p.Date)
            .Select(p => p.Close)
            .FirstOrDefaultAsync(ct);

        return Ok(new HoldingDto(
            existing.Id, instrument.Id, instrument.Symbol, existing.Quantity,
            existing.AverageCost, existing.TargetWeight, lastClose, existing.Quantity * lastClose));
    }
}
