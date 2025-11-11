using AmazoniaApi.Bot.Grpc;
using Discord;
using Discord.WebSocket;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace AmazoniaApi.Bot.Services;

public sealed class BotGrpcService : BotService.BotServiceBase
{
    private readonly DiscordSocketClient _discordClient;
    private readonly ApiClient _apiClient;
    private readonly ChannelPermissionManager _permissionManager;
    private readonly ILogger<BotGrpcService> _logger;

    public BotGrpcService(
        DiscordSocketClient discordClient,
        ApiClient apiClient,
        ChannelPermissionManager permissionManager,
        ILogger<BotGrpcService> logger)
    {
        _discordClient = discordClient;
        _apiClient = apiClient;
        _permissionManager = permissionManager;
        _logger = logger;
    }

    public override async Task<SendMessageResponse> SendMessageToChannel(SendMessageRequest request, ServerCallContext context)
    {
        if (!ulong.TryParse(request.ChannelId, out var channelId))
        {
            return new SendMessageResponse
            {
                Success = false,
                ErrorMessage = "Invalid channel id."
            };
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return new SendMessageResponse
            {
                Success = false,
                ErrorMessage = "Content is required."
            };
        }

        if (request.Content.Length > 2000)
        {
            return new SendMessageResponse
            {
                Success = false,
                ErrorMessage = "Content exceeds 2000 characters."
            };
        }

        try
        {
            if (_discordClient.GetChannel(channelId) is not IMessageChannel messageChannel)
            {
                return new SendMessageResponse
                {
                    Success = false,
                    ErrorMessage = "Channel not found or bot lacks access."
                };
            }

            _logger.LogInformation("Sending message to channel {ChannelId} for database message {MessageId}", channelId, request.DatabaseMessageId);
            var discordMessage = await messageChannel.SendMessageAsync(request.Content);

            await _apiClient.UpdateMessageDiscordIdAsync(request.DatabaseMessageId, discordMessage.Id, context.CancellationToken);

            return new SendMessageResponse
            {
                Success = true,
                DiscordMessageId = discordMessage.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to channel {ChannelId}", channelId);
            return new SendMessageResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public override async Task<VerifyTicketMessagesResponse> VerifyTicketMessages(VerifyTicketMessagesRequest request, ServerCallContext context)
    {
        var response = new VerifyTicketMessagesResponse();

        if (!ulong.TryParse(request.ChannelId, out var channelId))
        {
            response.ErrorMessage = "Invalid channel id.";
            return response;
        }

        if (_discordClient.GetChannel(channelId) is not IMessageChannel messageChannel)
        {
            response.ErrorMessage = "Channel not found or bot lacks access.";
            return response;
        }

        try
        {
            var discordMessages = await messageChannel.GetMessagesAsync(limit: 100).FlattenAsync();
            var messageLookup = discordMessages.ToDictionary(m => m.Id, m => m.Content?.Trim() ?? string.Empty);

            var missing = new List<int>();
            var mismatched = new List<int>();

            foreach (var message in request.Messages)
            {
                if (!messageLookup.TryGetValue(message.DiscordMessageId, out var content))
                {
                    missing.Add(message.DatabaseMessageId);
                    continue;
                }

                var expectedContent = (message.Content ?? string.Empty).Trim();
                if (!string.Equals(content, expectedContent, StringComparison.Ordinal))
                {
                    mismatched.Add(message.DatabaseMessageId);
                }
            }

            response.AllVerified = missing.Count == 0 && mismatched.Count == 0;
            response.MissingMessageIds.AddRange(missing);
            response.MismatchedMessageIds.AddRange(mismatched);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify messages for channel {ChannelId}", channelId);
            response.ErrorMessage = ex.Message;
            return response;
        }
    }

    public override async Task<UpdateChannelPermissionsResponse> UpdateChannelPermissions(UpdateChannelPermissionsRequest request, ServerCallContext context)
    {
        if (!ulong.TryParse(request.ChannelId, out var channelId))
        {
            return new UpdateChannelPermissionsResponse
            {
                Success = false,
                ErrorMessage = "Invalid channel id."
            };
        }

        _logger.LogInformation("Received channel permission update for {ChannelId}: readOnly={ReadOnly}", channelId, request.ReadOnly);

        try
        {
            var success = await _permissionManager.SetChannelReadOnlyAsync(channelId, request.ReadOnly);
            return new UpdateChannelPermissionsResponse
            {
                Success = success,
                ErrorMessage = success ? string.Empty : "Failed to update channel permissions."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update channel permissions for {ChannelId}", channelId);
            return new UpdateChannelPermissionsResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}