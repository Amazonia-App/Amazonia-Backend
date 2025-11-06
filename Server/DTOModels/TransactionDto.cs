using AmazoniaApi.Core.Models;

namespace AmazoniaApi.Server.DTOModels;

public class TransactionDto
{
    public int Id { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderDiscordName { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public string ReceiverDiscordName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; }

    public static TransactionDto FromModel(Transaction t) => new()
    {
        Id = t.Id,
        SenderId = t.SenderId,
        SenderDiscordName = t.SenderDiscordName,
        ReceiverId = t.ReceiverId,
        ReceiverDiscordName = t.ReceiverDiscordName,
        Amount = t.Amount,
        Timestamp = t.Timestamp
    };
}


