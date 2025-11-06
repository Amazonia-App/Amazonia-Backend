using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Server.DTOModels;

public class CreateTicketRequest
{
    [Required]
    [MaxLength(100)]
    public string ChannelId { get; set; } = string.Empty;
}

