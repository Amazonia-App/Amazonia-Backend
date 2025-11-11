using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AmazoniaApi.Server.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiKeyAuthAttribute : Attribute, IAuthorizationFilter
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var configuration = services.GetService(typeof(IConfiguration)) as IConfiguration;
        var logger = services.GetService(typeof(ILogger<ApiKeyAuthAttribute>)) as ILogger<ApiKeyAuthAttribute>;

        var expectedKey = configuration?["Bot:ApiKey"];
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            logger?.LogError("Bot API key is not configured.");
            context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey))
        {
            logger?.LogWarning("API key header missing from request.");
            context.Result = new UnauthorizedObjectResult(new { message = "API key is required." });
            return;
        }

        if (!IsValid(expectedKey, providedKey!))
        {
            logger?.LogWarning("Invalid API key provided from {RemoteIp}", context.HttpContext.Connection.RemoteIpAddress);
            context.Result = new UnauthorizedObjectResult(new { message = "Invalid API key." });
            return;
        }
    }

    private static bool IsValid(string expectedKey, string providedKey)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
        var providedBytes = Encoding.UTF8.GetBytes(providedKey);

        if (expectedBytes.Length != providedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}

