using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace AmazoniaApi.Core.Models;

public class AppUser : IdentityUser
{
    public ulong DiscordId { get; set; } // Link to Discord user ID
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal Balance { get; set; } = 0; // In-game currency
    
    [MaxLength(16)]
    public string MinecraftUsername { get; set; } = string.Empty; // Linked Minecraft username
    
    [Timestamp]
    public byte[]? RowVersion { get; set; } // For optimistic concurrency
}