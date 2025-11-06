using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Server.DTOModels;

public class AddToRoleRequest
{
    [Required]
    public string UserId { get; set; } = string.Empty;
    
    [Required]
    public string Role { get; set; } = string.Empty;
}

