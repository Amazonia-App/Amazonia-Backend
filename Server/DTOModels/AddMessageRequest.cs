using System.ComponentModel.DataAnnotations;

namespace Server.DTOModels;

public class AddMessageRequest
{
    [Required]
    [MaxLength(100)]
    public string ChannelId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;
}

