using System.ComponentModel.DataAnnotations;

namespace Core.Models;

public class Message
{
    [Key]
    public int Id { get; set; } // Property, not field
    
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty; // Use lowercase 'string', add default
    
    public string SenderId { get; set; } = string.Empty;// Property for navigation
    public DateTime Timestamp { get; set; } // Property, consider default like DateTime.UtcNow
}