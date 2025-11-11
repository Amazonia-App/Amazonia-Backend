using System.Net.Http.Json;
using System.Text.Json;
using AmazoniaApi.Bot.Models;
using AmazoniaApi.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AmazoniaApi.Bot.Services;

public sealed class ApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiClient> _logger;

    public ApiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(ApiClient));
        var apiKey = _configuration.GetValue<string>("Api:ApiKey");
        if (!string.IsNullOrWhiteSpace(apiKey) && !client.DefaultRequestHeaders.Contains("X-Api-Key"))
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        return client;
    }

    public async Task CreateTicketAsync(string channelId, ulong discordUserId, string title, CancellationToken cancellationToken = default)
    {
        var request = new CreateTicketRequest
        {
            ChannelId = channelId,
            DiscordUserId = discordUserId,
            Title = title
        };

        using var client = CreateClient();
        _logger.LogInformation("Creating ticket for channel {ChannelId} and user {DiscordUserId}", channelId, discordUserId);

        var response = await client.PostAsJsonAsync("/api/Ticket/Bot/CreateTicket", request, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, "create ticket");
    }

    public async Task StoreMessageAsync(string channelId, ulong senderDiscordId, string content, ulong discordMessageId, CancellationToken cancellationToken = default)
    {
        var request = new StoreMessageRequest
        {
            ChannelId = channelId,
            SenderDiscordId = senderDiscordId,
            Content = content,
            DiscordMessageId = discordMessageId
        };

        using var client = CreateClient();
        _logger.LogDebug("Storing message {DiscordMessageId} in channel {ChannelId}", discordMessageId, channelId);

        var response = await client.PostAsJsonAsync("/api/Ticket/Bot/StoreMessage", request, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, "store message");
    }

    public async Task UpdateMessageDiscordIdAsync(int messageId, ulong discordMessageId, CancellationToken cancellationToken = default)
    {
        var request = new UpdateMessageDiscordIdRequest
        {
            MessageId = messageId,
            DiscordMessageId = discordMessageId
        };

        using var client = CreateClient();
        _logger.LogDebug("Updating message {MessageId} with Discord ID {DiscordMessageId}", messageId, discordMessageId);

        var response = await client.PatchAsJsonAsync("/api/Ticket/Bot/UpdateMessageDiscordId", request, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, "update Discord message ID");
    }

    public async Task UpdateChannelPermissionsAsync(string channelId, bool readOnly, CancellationToken cancellationToken = default)
    {
        var request = new UpdateChannelPermissionsRequest
        {
            ChannelId = channelId,
            ReadOnly = readOnly
        };

        _logger.LogInformation("Updating channel {ChannelId} permissions: readOnly={ReadOnly}", channelId, readOnly);

        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/Ticket/Bot/UpdateChannelPermissions", request, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, "update channel permissions");
    }

    public async Task<IReadOnlyCollection<TicketInfo>> GetOpenTicketsAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/Ticket/Bot/GetOpenTickets", cancellationToken);
        await EnsureSuccessAsync(response, "retrieve open tickets");

        var data = await response.Content.ReadFromJsonAsync<List<BotTicketSummary>>(SerializerOptions, cancellationToken);
        if (data is null)
        {
            return Array.Empty<TicketInfo>();
        }

        var tickets = new List<TicketInfo>(data.Count);
        foreach (var summary in data)
        {
            if (!ulong.TryParse(summary.ChannelId, out var channelId))
            {
                _logger.LogWarning("Skipping ticket summary with invalid channel ID: {ChannelId}", summary.ChannelId);
                continue;
            }

            tickets.Add(new TicketInfo(channelId, summary.CreatorDiscordId, summary.Status));
        }

        return tickets;
    }

    public async Task<TicketInfo?> GetTicketInfoAsync(string channelId, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/api/Ticket/Bot/GetTicketInfo/{channelId}", cancellationToken);
        
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "retrieve ticket info");

        var data = await response.Content.ReadFromJsonAsync<BotTicketSummary>(SerializerOptions, cancellationToken);
        if (data is null)
        {
            return null;
        }

        if (!ulong.TryParse(data.ChannelId, out var parsedChannelId))
        {
            _logger.LogWarning("Received ticket summary with invalid channel ID: {ChannelId}", data.ChannelId);
            return null;
        }

        return new TicketInfo(parsedChannelId, data.CreatorDiscordId, data.Status);
    }

    public async Task UpdateTicketStatusAsync(string channelId, TicketStatus status, CancellationToken cancellationToken = default)
    {
        var request = new Models.UpdateTicketStatusRequest
        {
            ChannelId = channelId,
            Status = status
        };

        using var client = CreateClient();
        _logger.LogInformation("Updating ticket status for channel {ChannelId} to {Status}", channelId, status);

        var response = await client.PutAsJsonAsync("/api/Ticket/Bot/UpdateStatus", request, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, "update ticket status");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string context)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogError("Failed to {Context}. Status: {StatusCode}. Response: {Error}", context, response.StatusCode, errorContent);
        response.EnsureSuccessStatusCode();
    }

    private sealed record BotTicketSummary(string ChannelId, ulong CreatorDiscordId, TicketStatus Status);
}