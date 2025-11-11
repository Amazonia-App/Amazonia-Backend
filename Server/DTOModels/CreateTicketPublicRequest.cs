using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Server.DTOModels;

public class CreateTicketPublicRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(30)]
    public string Title { get; set; } = string.Empty;
}

