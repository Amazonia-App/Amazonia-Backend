using System.ComponentModel.DataAnnotations;

namespace AmazoniaApi.Server.DTOModels;

public class RemoveFromRoleRequest
{
    [Required]
    public string UserId { get; set; } = string.Empty;
    
    [Required]
    public string Role { get; set; } = string.Empty;
}

