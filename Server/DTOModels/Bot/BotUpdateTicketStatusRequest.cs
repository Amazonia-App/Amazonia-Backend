using System.ComponentModel.DataAnnotations;
using AmazoniaApi.Core.Models;

namespace AmazoniaApi.Server.DTOModels.Bot;

public sealed class BotUpdateTicketStatusRequest
{
    [Required]
    [MaxLength(100)]
    public string ChannelId { get; set; } = string.Empty;
    
    [Required]
    public TicketStatus Status { get; set; }
}

