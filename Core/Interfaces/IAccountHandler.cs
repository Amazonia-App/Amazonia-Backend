using Core.Models;

namespace Core.Interfaces;

public interface IAccountHandler
{
    Task ChangeMinecraftUsernameAsync(string newMinecraftUsername);

    Task<AppUser> GetUserAsync(string userId);

    Task<(List<AppUser> Users, int TotalCount)> FindUserAsync(string query, int page, int pageSize);
}