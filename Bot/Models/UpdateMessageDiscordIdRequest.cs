using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Bot.Models;

public sealed class UpdateMessageDiscordIdRequest
{
    [Required]
    [Range(1, int.MaxValue)]
    public int MessageId { get; set; }

    [Required]
    public ulong DiscordMessageId { get; set; }
}

