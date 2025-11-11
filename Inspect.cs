using System;
using System.Linq;
using System.Reflection;
using Discord.WebSocket;
using Discord.Rest;

class Program
{
    static void Main()
    {
        var type = typeof(DiscordSocketRestClient);
        Console.WriteLine($"Methods on {type.FullName}:");
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy))
        {
            if (method.Name.Contains("Bulk", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("ApplicationCommand", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(method);
            }
        }
    }
}
