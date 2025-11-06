using System.ComponentModel.DataAnnotations;
using Core.Models;

namespace Server.DTOModels;

public class UpdateTicketStatusRequest
{
    [Required]
    [MaxLength(100)]
    public string ChannelId { get; set; } = string.Empty;
    
    [Required]
    public TicketStatus Status { get; set; }
}

