using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Server.DTOModels.Bot;

public sealed class BotUpdateChannelPermissionsRequest
{
    [Required]
    [MaxLength(100)]
    public string ChannelId { get; set; } = string.Empty;

    [Required]
    public bool ReadOnly { get; set; }
}

