using FiscalFox.Api.Data;
using FiscalFox.Api.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiscalFox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InstrumentsController : ControllerBase
{
    private readonly FiscalFoxDbContext _db;

    public InstrumentsController(FiscalFoxDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<InstrumentDto>>> GetAll(CancellationToken ct)
    {
        var instruments = await _db.Instruments.OrderBy(i => i.Symbol).ToListAsync(ct);
        var result = new List<InstrumentDto>();
        foreach (var i in instruments)
        {
            var lastClose = await _db.PriceBars
                .Where(p => p.InstrumentId == i.Id)
                .OrderByDescending(p => p.Date)
                .Select(p => (decimal?)p.Close)
                .FirstOrDefaultAsync(ct);
            result.Add(new InstrumentDto(i.Id, i.Symbol, i.Name, i.AssetClass, i.Currency, lastClose));
        }
        return Ok(result);
    }

    [HttpGet("{symbol}/prices")]
    public async Task<ActionResult<IEnumerable<object>>> GetPrices(string symbol, [FromQuery] int limit = 260, CancellationToken ct = default)
    {
        var instrument = await _db.Instruments.FirstOrDefaultAsync(i => i.Symbol == symbol, ct);
        if (instrument is null)
            return NotFound();

        limit = Math.Clamp(limit, 1, 2000);

        var prices = await _db.PriceBars
            .Where(p => p.InstrumentId == instrument.Id)
            .OrderByDescending(p => p.Date)
            .Take(limit)
            .OrderBy(p => p.Date)
            .Select(p => new { p.Date, p.Close })
            .ToListAsync(ct);

        return Ok(prices);
    }
}
