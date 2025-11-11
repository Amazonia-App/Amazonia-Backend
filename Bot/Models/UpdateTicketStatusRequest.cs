using AmazoniaApi.Core.Models;

namespace AmazoniaApi.Bot.Models;

public sealed class UpdateTicketStatusRequest
{
    public string ChannelId { get; set; } = string.Empty;
    public TicketStatus Status { get; set; }
}

