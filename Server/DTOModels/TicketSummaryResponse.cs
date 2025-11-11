using AmazoniaApi.Core.Models;

namespace AmazoniaApi.Server.DTOModels;

public class TicketSummaryResponse
{
    public string ChannelId { get; set; } = string.Empty;
    public string? CreatorId { get; set; }
    public TicketStatus Status { get; set; }
    public string Title { get; set; } = string.Empty;
}

