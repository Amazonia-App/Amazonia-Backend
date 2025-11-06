using AmazoniaApi.Core.Models;
using Microsoft.AspNetCore.Identity;

namespace AmazoniaApi.Server.Seeders;

public class RoleSeeder(RoleManager<IdentityRole> roleManager)
{
    public async Task SeedRolesAsync()
    {
        foreach (var roleName in Roles.GetAllRoles)
        {
            var roleExists = await roleManager.RoleExistsAsync(roleName);
            if (!roleExists)
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }
    }
}
