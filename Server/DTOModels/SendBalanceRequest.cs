using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Server.DTOModels;

public class SendBalanceRequest
{
    [Required]
    public string ReceiverId { get; set; } = string.Empty;
    
    [Required]
    [Range(10.01, double.MaxValue, ErrorMessage = "Amount must be greater than 10")]
    public decimal Amount { get; set; }
}

