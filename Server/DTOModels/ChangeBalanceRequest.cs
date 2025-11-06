using System.ComponentModel.DataAnnotations;

namespace Server.DTOModels;

public class ChangeBalanceRequest
{
    [Required]
    public string UserId { get; set; } = string.Empty;
    
    [Required]
    public decimal Amount { get; set; }
}

