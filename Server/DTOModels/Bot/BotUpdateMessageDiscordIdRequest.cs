using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Server.DTOModels.Bot;

public sealed class BotUpdateMessageDiscordIdRequest
{
    [Required]
    [Range(1, int.MaxValue)]
    public int MessageId { get; set; }

    [Required]
    public ulong DiscordMessageId { get; set; }
}

