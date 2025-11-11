using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Server.DTOModels.Bot;

public sealed class BotCreateTicketRequest
{
    [Required]
    [MaxLength(100)]
    public string ChannelId { get; set; } = string.Empty;

    [Required]
    public ulong DiscordUserId { get; set; }
}

