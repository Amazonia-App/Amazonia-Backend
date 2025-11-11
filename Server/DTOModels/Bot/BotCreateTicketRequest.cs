using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Server.DTOModels.Bot;

public sealed class BotCreateTicketRequest
{
    // ChannelId is no longer required - it will be created by the bot
    
    [Required]
    public ulong DiscordUserId { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(30)]
    public string Title { get; set; } = string.Empty;
}

