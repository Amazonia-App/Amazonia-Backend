using System.Security.Claims;
using AmazoniaApi.Core.Interfaces;
using AmazoniaApi.Core.Models;
using Microsoft.AspNetCore.Identity;

namespace AmazoniaApi.Server.Handlers;

public class HelperMethods(ClaimsPrincipal user, UserManager<AppUser> userManager) : IHelperMethods
{
    public async Task<AppUser> GetLoggedInUserAsync()
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            throw new ArgumentException("Not Logged In");
        }
        
        var appUser = await userManager.FindByIdAsync(userId);

        return appUser ?? throw new ArgumentException("Not Logged In");
    }
    
    public async Task<AppUser> GetUserByIdAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            throw new ArgumentException("User ID is null or empty");
        }
        
        var appUser = await userManager.FindByIdAsync(userId);

        return appUser ?? throw new ArgumentException("User not found");
    }
}