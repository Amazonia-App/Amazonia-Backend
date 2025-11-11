using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Bot.Models;

public sealed class CreateTicketRequest
{
    [Required]
    [MaxLength(100)]
    public string ChannelId { get; set; } = string.Empty;

    [Required]
    public ulong DiscordUserId { get; set; }
}

