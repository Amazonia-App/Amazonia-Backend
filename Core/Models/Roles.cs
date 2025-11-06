namespace Core.Models;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string MinecraftNameAdded = "MinecraftNameAdded";
    
    public static List<string> GetAllRoles =>
    [
        Admin,
        Staff,
        MinecraftNameAdded
    ];
    
    public static bool IsValidRole(string role) => GetAllRoles.Contains(role);
}