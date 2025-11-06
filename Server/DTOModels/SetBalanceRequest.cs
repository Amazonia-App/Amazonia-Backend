using System.ComponentModel.DataAnnotations;

namespace Server.DTOModels;

public class SetBalanceRequest
{
    [Required]
    public string UserId { get; set; } = string.Empty;
    
    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "Balance cannot be negative")]
    public decimal Amount { get; set; }
}

