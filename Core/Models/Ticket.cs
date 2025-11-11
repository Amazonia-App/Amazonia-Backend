using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Core.Models;

public class Ticket
{
    [Key]
    [MaxLength(100)]
    public string ChannelId { get; set; } = string.Empty; // Property, use lowercase 'string'
    public List<Message> Messages { get; set; } = new List<Message>(); // Initialize to avoid null
    public string? CreatorId { get; set; } = string.Empty; // Property for navigation
    public TicketStatus Status { get; set; } // Property for enum
    [MaxLength(30)]
    public string Title { get; set; } = string.Empty; // Title of the ticket
}