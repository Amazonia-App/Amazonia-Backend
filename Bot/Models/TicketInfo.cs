using AmazoniaApi.Core.Models;

namespace AmazoniaApi.Bot.Models;

public sealed class TicketInfo
{
    public TicketInfo(ulong channelId, ulong creatorDiscordId, TicketStatus status, DateTime? createdAt = null)
    {
        ChannelId = channelId;
        CreatorDiscordId = creatorDiscordId;
        Status = status;
        CreatedAt = createdAt ?? DateTime.UtcNow;
    }

    public ulong ChannelId { get; }

    public ulong CreatorDiscordId { get; }

    public TicketStatus Status { get; private set; }

    public DateTime CreatedAt { get; }

    public bool IsReadOnly() =>
        Status is TicketStatus.Completed or TicketStatus.Archived or TicketStatus.Closed;

    public bool IsActive() =>
        Status is TicketStatus.Created or TicketStatus.Open or TicketStatus.Received or TicketStatus.UnderReview;

    public void UpdateStatus(TicketStatus status) => Status = status;
}

