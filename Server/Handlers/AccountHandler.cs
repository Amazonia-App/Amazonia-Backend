using Core.Interfaces;
using Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Server.Handlers;

public class AccountHandler(UserManager<AppUser> userManager, IHelperMethods helperMethods) : IAccountHandler
{
    public async Task ChangeMinecraftUsernameAsync(string newMinecraftUsername)
    {
        // Pre validation
        if (string.IsNullOrWhiteSpace(newMinecraftUsername) || newMinecraftUsername.Length > 16)
        {
            throw new ArgumentException("Invalid Minecraft username, make sure your username is correct to avoid money transfer issues.");
        }
        
        var loggedInUser = await helperMethods.GetLoggedInUserAsync();
        
        loggedInUser.MinecraftUsername = newMinecraftUsername;
        var result = await userManager.UpdateAsync(loggedInUser);

        if (!result.Succeeded)
        {
            throw new ArgumentException("Failed to update Minecraft username");
        }
        
        // Add the user to the MinecraftNameChanged role if not already in it
        if (await userManager.IsInRoleAsync(loggedInUser, Roles.MinecraftNameAdded)) return;
        
        var roleResult = await userManager.AddToRoleAsync(loggedInUser, Roles.MinecraftNameAdded);
        if (!roleResult.Succeeded)
        {
            throw new ArgumentException("Failed to add user to MinecraftNameAdded role");
        }
    }

    public async Task<AppUser> GetUserAsync(string userId)
    {
        // Pre validation
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be empty");

        // Execution
        var user = await helperMethods.GetUserByIdAsync(userId);

        return user;
    }
    
    public async Task<(List<AppUser> Users, int TotalCount)> FindUserAsync(string query, int page, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty");
        
        var queryable = userManager.Users
            .Where(u => u.UserName != null && (u.UserName.Contains(query) || u.MinecraftUsername.Contains(query)))
            .OrderBy(u => u.UserName);

        var totalCount = await queryable.CountAsync();
        
        var users = await queryable
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (users, totalCount);
    }
}