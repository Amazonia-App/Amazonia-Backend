using AmazoniaApi.Core.Models;

namespace AmazoniaApi.Server.DTOModels;

public class AppUserDto(AppUser appUser, IEnumerable<string>? roles = null)
{
    public string Id { get; set; } = appUser.Id;
    public ulong DiscordId { get; set; } = appUser.DiscordId; // Link to Discord user ID
    public string? DiscordName { get; set; } = appUser.UserName; // Username
    public decimal Balance { get; set; } = appUser.Balance; // In-game currency
    public string MinecraftUsername { get; set; } = appUser.MinecraftUsername; // Linked Minecraft username
    public List<string> Roles { get; set; } = roles?.ToList() ?? new();
}