using FiscalFox.Api.Dtos;
using FiscalFox.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FiscalFox.Api.Controllers;

/// <summary>
/// Account transactions. Posting a Buy/Sell/Dividend/Deposit/Withdrawal/Fee
/// updates the account's holdings, cash balance and cost basis via the
/// <see cref="TransactionService"/>.
/// </summary>
[ApiController]
[Route("api/accounts/{accountId:int}/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly TransactionService _transactions;

    public TransactionsController(TransactionService transactions) => _transactions = transactions;

    /// <summary>List an account's transactions, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TransactionDto>>> GetAll(int accountId, CancellationToken ct)
    {
        var list = await _transactions.ListAsync(accountId, ct);
        if (list is null)
            return NotFound($"Account {accountId} not found.");
        return Ok(list);
    }

    /// <summary>
    /// Apply a transaction to the account. Buys/Sells adjust the matching holding
    /// and cost basis; all kinds adjust the cash balance.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TransactionDto>> Create(int accountId, CreateTransactionDto dto, CancellationToken ct)
    {
        var result = await _transactions.ApplyAsync(accountId, dto, ct);
        if (result.NotFound)
            return NotFound(result.Error);
        if (result.Error is not null)
            return BadRequest(result.Error);
        return CreatedAtAction(nameof(GetAll), new { accountId }, result.Transaction);
    }
}
