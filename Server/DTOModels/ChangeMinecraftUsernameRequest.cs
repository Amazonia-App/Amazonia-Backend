using System.ComponentModel.DataAnnotations;

namespace Server.DTOModels;

public class ChangeMinecraftUsernameRequest
{
    [Required]
    [MaxLength(16)]
    [MinLength(1)]
    public string MinecraftUsername { get; set; } = string.Empty;
}

