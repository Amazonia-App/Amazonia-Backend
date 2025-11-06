using Core.Interfaces;
using Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Server.DBContext;
using Server.DTOModels;

namespace Server.Handlers;

public class BankHandler(
    UserManager<AppUser> userManager, 
    IHelperMethods helperMethods,
    AppDbContext context,
    ILogger<BankHandler> logger) : IBankHandler
{
    public async Task<decimal> GetBalanceAsync()
    {
        var user = await helperMethods.GetLoggedInUserAsync();
        return user.Balance;
    }
    
    public async Task SendBalanceAsync(decimal amount, string receiverId)
    {
        // Pre validation
        if (amount <= 10)
        {
            throw new ArgumentException("You can only send payments above 10");
        }

        // Validate amount precision
        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentException("Amount must have at most 2 decimal places");
        }

        const int maxRetries = 3;
        int retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    var senderId = (await helperMethods.GetLoggedInUserAsync()).Id;
                    var sender = await context.Users.FindAsync(senderId);
                    var receiver = await context.Users.FindAsync(receiverId);
                    
                    if (sender == null || receiver == null)
                    {
                        throw new ArgumentException("User not found");
                    }

                    if (sender.Id == receiver.Id)
                    {
                        throw new ArgumentException("You cannot send money to yourself");
                    }

                    // Reload to get fresh RowVersion for concurrency check
                    await context.Entry(sender).ReloadAsync();
                    await context.Entry(receiver).ReloadAsync();

                    // Execution
                    if (sender.Balance < amount)
                    {
                        throw new ArgumentException("Insufficient funds");
                    }
                    
                    sender.Balance -= amount;
                    receiver.Balance += amount;

                    // Prevent negative balance
                    if (sender.Balance < 0)
                    {
                        throw new ArgumentException("Sender balance would become negative");
                    }

                    // Record transaction
                    var transactionRecord = new Transaction
                    {
                        SenderId = sender.Id,
                        SenderDiscordName = sender.UserName ?? string.Empty,
                        ReceiverId = receiver.Id,
                        ReceiverDiscordName = receiver.UserName ?? string.Empty,
                        Amount = amount,
                        Timestamp = DateTime.UtcNow
                    };
                    context.Transactions.Add(transactionRecord);

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    logger.LogInformation(
                        "Balance transfer completed: SenderId={SenderId}, ReceiverId={ReceiverId}, Amount={Amount}",
                        sender.Id, receiver.Id, amount);

                    return;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    throw new InvalidOperationException("Failed to complete transfer after retries due to concurrent updates");
                }
                // Wait a bit before retrying (exponential backoff)
                await Task.Delay(100 * retryCount);
            }
        }
    }

    public async Task<List<Transaction>> GetTransactionsAsync([FromQuery] int amount = 10)
    {
        var user = await helperMethods.GetLoggedInUserAsync();
        var transactions = await context.Transactions
            .Where(t => t.SenderId == user.Id || t.ReceiverId == user.Id)
            .OrderByDescending(t => t.Timestamp)
            .Take(amount)
            .ToListAsync();
        return transactions;
    }
    
    public async Task ChangeBalanceAsync(string userId, decimal amount)
    {
        // Pre validation
        if (amount == 0)
        {
            throw new ArgumentException("Amount must be non-zero");
        }

        // Validate amount precision
        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentException("Amount must have at most 2 decimal places");
        }

        const int maxRetries = 3;
        int retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    var user = await context.Users.FindAsync(userId);
                    if (user == null)
                    {
                        throw new ArgumentException("User not found");
                    }

                    // Reload to get fresh RowVersion
                    await context.Entry(user).ReloadAsync();
                    
                    // Execution
                    user.Balance += amount;
                    if (user.Balance < 0)
                    {
                        user.Balance = 0; // Prevent negative balance
                    }

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    logger.LogInformation(
                        "Balance changed: UserId={UserId}, Amount={Amount}, NewBalance={NewBalance}",
                        userId, amount, user.Balance);

                    return;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    throw new InvalidOperationException("Failed to change balance after retries due to concurrent updates");
                }
                await Task.Delay(100 * retryCount);
            }
        }
    }
    
    public async Task SetBalanceAsync(string userId, decimal amount)
    {
        // Pre validation
        if (amount < 0)
        {
            throw new ArgumentException("Balance cannot be set to a negative amount");
        }

        // Validate amount precision
        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentException("Amount must have at most 2 decimal places");
        }

        const int maxRetries = 3;
        int retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    var user = await context.Users.FindAsync(userId);
                    if (user == null)
                    {
                        throw new ArgumentException("User not found");
                    }

                    // Reload to get fresh RowVersion
                    await context.Entry(user).ReloadAsync();
                    
                    // Execution
                    user.Balance = amount;

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    logger.LogInformation(
                        "Balance set: UserId={UserId}, NewBalance={NewBalance}",
                        userId, amount);

                    return;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    throw new InvalidOperationException("Failed to set balance after retries due to concurrent updates");
                }
                await Task.Delay(100 * retryCount);
            }
        }
    }
}