using Core.Interfaces;
using Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.DTOModels;

namespace Server.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class BankController(IBankHandler bankHandler, ILogger<BankController> logger) : ControllerBase
{
    [HttpGet("getBalance")]
    public async Task<ActionResult> GetBalance()
    {
        try
        {
            var balance = await bankHandler.GetBalanceAsync();
            logger.LogInformation("Balance retrieved for user");
            return Ok(new { balance });
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Balance retrieval failed: {Error}", e.Message);
            return BadRequest(new {message = e.Message});
        }
    }
    
    [HttpPost("sendBalance")]
    public async Task<ActionResult> SendBalance([FromBody] SendBalanceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await bankHandler.SendBalanceAsync(request.Amount, request.ReceiverId);
            logger.LogInformation("Balance transfer initiated: ReceiverId={ReceiverId}, Amount={Amount}", request.ReceiverId, request.Amount);
            return Ok(new { message = "Transaction successful" });
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Balance transfer failed: {Error}", e.Message);
            return BadRequest(new {message = e.Message});
        }
        catch (InvalidOperationException e)
        {
            logger.LogError("Balance transfer error: {Error}", e.Message);
            return StatusCode(500, new {message = e.Message});
        }
    }

    //endpoint to get all transactions for the logged in user
    [HttpGet("getTransactions")]
    public async Task<ActionResult> GetTransactions([FromQuery] int amount = 10)
    {
        var transactions = await bankHandler.GetTransactionsAsync(amount);
        return Ok(new { transactions = transactions.Select(TransactionDto.FromModel).ToList() });
    }
    
    //add balance admin only
    [Authorize(Roles = Roles.Admin)]
    [HttpPost("changeBalance")]
    public async Task<ActionResult> ChangeBalance([FromBody] ChangeBalanceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await bankHandler.ChangeBalanceAsync(request.UserId, request.Amount);
            logger.LogInformation("Balance changed by admin: UserId={UserId}, Amount={Amount}", request.UserId, request.Amount);
            return Ok(new { message = "Balance changed successfully", newBalance = await bankHandler.GetBalanceAsync()});
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Balance change failed: {Error}", e.Message);
            return BadRequest(new {message = e.Message});
        }
        catch (InvalidOperationException e)
        {
            logger.LogError("Balance change error: {Error}", e.Message);
            return StatusCode(500, new {message = e.Message});
        }
    }
    
    //set the balance admin only
    [Authorize(Roles = Roles.Admin)]
    [HttpPost("setBalance")]
    public async Task<ActionResult> SetBalance([FromBody] SetBalanceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await bankHandler.SetBalanceAsync(request.UserId, request.Amount);
            logger.LogInformation("Balance set by admin: UserId={UserId}, Amount={Amount}", request.UserId, request.Amount);
            return Ok(new { message = "Balance set successfully", newBalance = await bankHandler.GetBalanceAsync()});
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Balance set failed: {Error}", e.Message);
            return BadRequest(new {message = e.Message});
        }
        catch (InvalidOperationException e)
        {
            logger.LogError("Balance set error: {Error}", e.Message);
            return StatusCode(500, new {message = e.Message});
        }
    }
}