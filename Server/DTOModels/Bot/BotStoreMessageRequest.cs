using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Server.DTOModels.Bot;

public sealed class BotStoreMessageRequest
{
    [Required]
    [MaxLength(100)]
    public string ChannelId { get; set; } = string.Empty;

    [Required]
    public ulong SenderDiscordId { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    [Required]
    public ulong DiscordMessageId { get; set; }
}

