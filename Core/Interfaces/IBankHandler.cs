namespace Core.Interfaces;

using Core.Models;
public interface IBankHandler
{
    public Task<decimal> GetBalanceAsync();

    public Task SendBalanceAsync(decimal amount, string receiverId);

    public Task<List<Transaction>> GetTransactionsAsync(int amount = 10);

    public Task ChangeBalanceAsync(string userId, decimal amount);

    public Task SetBalanceAsync(string userId, decimal amount);
}