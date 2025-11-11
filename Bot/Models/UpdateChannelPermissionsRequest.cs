using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Bot.Models;

public sealed class UpdateChannelPermissionsRequest
{
    [Required]
    [MaxLength(100)]
    public string ChannelId { get; set; } = string.Empty;

    [Required]
    public bool ReadOnly { get; set; }
}

