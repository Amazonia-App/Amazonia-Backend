using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AmazoniaApi.Bot.Grpc;
using AmazoniaApi.Core.Interfaces;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AmazoniaApi.Server.Services;

public class BotGrpcClient : IBotGrpcClient, IDisposable
{
    private readonly ILogger<BotGrpcClient> _logger;
    private readonly GrpcChannel _channel;
    private readonly BotService.BotServiceClient _client;
    private bool _disposed;

    public BotGrpcClient(IConfiguration configuration, ILogger<BotGrpcClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var botGrpcUrl = configuration["Bot:GrpcUrl"];

        if (string.IsNullOrWhiteSpace(botGrpcUrl))
        {
            throw new InvalidOperationException("Bot:GrpcUrl is not configured. Please set it in appsettings.json or environment variables.");
        }

        GrpcChannelOptions? channelOptions = null;

        if (botGrpcUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            channelOptions = new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true
                }
            };
        }

        _channel = channelOptions is null
            ? GrpcChannel.ForAddress(botGrpcUrl)
            : GrpcChannel.ForAddress(botGrpcUrl, channelOptions);

        _client = new BotService.BotServiceClient(_channel);

        _logger.LogInformation("BotGrpcClient initialized with Bot:GrpcUrl {BotGrpcUrl}", botGrpcUrl);
    }

    public async Task<(bool success, ulong discordMessageId, string? errorMessage)> SendMessageToChannelAsync(
        string channelId,
        string content,
        int databaseMessageId,
        CancellationToken cancellationToken = default)
    {
        var request = new SendMessageRequest
        {
            ChannelId = channelId,
            Content = content,
            DatabaseMessageId = databaseMessageId,
        };

        try
        {
            _logger.LogInformation(
                "Sending message to channel {ChannelId} for DatabaseMessageId {DatabaseMessageId}",
                channelId,
                databaseMessageId);

            var response = await _client.SendMessageToChannelAsync(request, cancellationToken: cancellationToken);

            if (response.Success)
            {
                _logger.LogInformation(
                    "Sent message to channel {ChannelId}, DatabaseMessageId={DatabaseMessageId}, DiscordMessageId={DiscordMessageId}",
                    channelId,
                    databaseMessageId,
                    response.DiscordMessageId);
            }
            else
            {
                _logger.LogWarning(
                    "Bot reported failure sending message to channel {ChannelId}, DatabaseMessageId={DatabaseMessageId}. Error: {ErrorMessage}",
                    channelId,
                    databaseMessageId,
                    response.ErrorMessage);
            }

            return (response.Success, response.DiscordMessageId, response.ErrorMessage);
        }
        catch (RpcException ex)
        {
            _logger.LogError(
                ex,
                "Error sending message to channel {ChannelId} for DatabaseMessageId {DatabaseMessageId}",
                channelId,
                databaseMessageId);

            return (false, 0, ex.Message);
        }
    }

    public async Task<(bool allVerified, List<int> missingMessageIds, List<int> mismatchedMessageIds, string? errorMessage)> VerifyTicketMessagesAsync(
        string channelId,
        List<(int databaseMessageId, ulong discordMessageId, string content)> messages,
        CancellationToken cancellationToken = default)
    {
        var request = new VerifyTicketMessagesRequest
        {
            ChannelId = channelId,
        };

        foreach (var message in messages)
        {
            request.Messages.Add(new MessageToVerify
            {
                DatabaseMessageId = message.databaseMessageId,
                DiscordMessageId = message.discordMessageId,
                Content = message.content,
            });
        }

        try
        {
            _logger.LogInformation(
                "Verifying {MessageCount} messages in channel {ChannelId}",
                request.Messages.Count,
                channelId);

            var response = await _client.VerifyTicketMessagesAsync(request, cancellationToken: cancellationToken);

            if (response.AllVerified)
            {
                _logger.LogInformation(
                    "All messages verified for channel {ChannelId}",
                    channelId);
            }
            else
            {
                _logger.LogWarning(
                    "Verification completed for channel {ChannelId} with Missing={MissingCount}, Mismatched={MismatchedCount}",
                    channelId,
                    response.MissingMessageIds.Count,
                    response.MismatchedMessageIds.Count);
            }

            var missingMessageIds = response.MissingMessageIds.ToList();
            var mismatchedMessageIds = response.MismatchedMessageIds.ToList();

            return (response.AllVerified, missingMessageIds, mismatchedMessageIds, response.ErrorMessage);
        }
        catch (RpcException ex)
        {
            _logger.LogError(
                ex,
                "Error verifying messages for channel {ChannelId}",
                channelId);

            return (false, new List<int>(), new List<int>(), ex.Message);
        }
    }

    public async Task<(bool success, string? errorMessage)> UpdateChannelPermissionsAsync(
        string channelId,
        bool readOnly,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateChannelPermissionsRequest
        {
            ChannelId = channelId,
            ReadOnly = readOnly,
        };

        try
        {
            _logger.LogInformation(
                "Updating channel permissions for {ChannelId} to ReadOnly={ReadOnly}",
                channelId,
                readOnly);

            var response = await _client.UpdateChannelPermissionsAsync(request, cancellationToken: cancellationToken);

            if (response.Success)
            {
                _logger.LogInformation(
                    "Updated channel permissions for {ChannelId}",
                    channelId);
            }
            else
            {
                _logger.LogWarning(
                    "Bot reported failure updating channel permissions for {ChannelId}. Error: {ErrorMessage}",
                    channelId,
                    response.ErrorMessage);
            }

            return (response.Success, response.ErrorMessage);
        }
        catch (RpcException ex)
        {
            _logger.LogError(
                ex,
                "Error updating channel permissions for {ChannelId}",
                channelId);

            return (false, ex.Message);
        }
    }

    public async Task<(bool success, List<(ulong discordMessageId, string content, ulong authorDiscordId, long timestamp)> messages, string? errorMessage)> GetAllChannelMessagesAsync(
        string channelId,
        CancellationToken cancellationToken = default)
    {
        var request = new GetAllChannelMessagesRequest
        {
            ChannelId = channelId,
        };

        try
        {
            _logger.LogInformation("Fetching all messages from channel {ChannelId}", channelId);

            var response = await _client.GetAllChannelMessagesAsync(request, cancellationToken: cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning(
                    "Bot reported failure fetching messages from channel {ChannelId}. Error: {ErrorMessage}",
                    channelId,
                    response.ErrorMessage);
                return (false, new List<(ulong, string, ulong, long)>(), response.ErrorMessage);
            }

            var messages = response.Messages
                .Select(m => (m.DiscordMessageId, m.Content, m.AuthorDiscordId, m.Timestamp))
                .ToList();

            _logger.LogInformation(
                "Fetched {MessageCount} messages from channel {ChannelId}",
                messages.Count,
                channelId);

            return (true, messages, null);
        }
        catch (RpcException ex)
        {
            _logger.LogError(
                ex,
                "Error fetching messages from channel {ChannelId}",
                channelId);

            return (false, new List<(ulong, string, ulong, long)>(), ex.Message);
        }
    }

    public async Task<(bool success, string? channelId, string? errorMessage)> CreateTicketChannelAsync(
        ulong discordUserId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateTicketChannelRequest
        {
            DiscordUserId = discordUserId,
            Title = title ?? string.Empty,
        };

        try
        {
            _logger.LogInformation("Requesting bot to create ticket channel for Discord user {DiscordUserId}", discordUserId);

            var response = await _client.CreateTicketChannelAsync(request, cancellationToken: cancellationToken);

            if (response.Success)
            {
                _logger.LogInformation(
                    "Bot created ticket channel {ChannelId} for Discord user {DiscordUserId}",
                    response.ChannelId,
                    discordUserId);
                return (true, response.ChannelId, null);
            }
            else
            {
                _logger.LogWarning(
                    "Bot reported failure creating ticket channel for Discord user {DiscordUserId}. Error: {ErrorMessage}",
                    discordUserId,
                    response.ErrorMessage);
                return (false, null, response.ErrorMessage);
            }
        }
        catch (RpcException ex)
        {
            _logger.LogError(
                ex,
                "Error requesting bot to create ticket channel for Discord user {DiscordUserId}",
                discordUserId);

            return (false, null, ex.Message);
        }
    }

    public async Task<(bool success, string? errorMessage)> MoveTicketChannelToCategoryAsync(
        string channelId,
        string status,
        CancellationToken cancellationToken = default)
    {
        var request = new MoveTicketChannelToCategoryRequest
        {
            ChannelId = channelId,
            Status = status,
        };

        try
        {
            _logger.LogInformation(
                "Moving channel {ChannelId} to category based on status {Status}",
                channelId,
                status);

            var response = await _client.MoveTicketChannelToCategoryAsync(request, cancellationToken: cancellationToken);

            if (response.Success)
            {
                _logger.LogInformation(
                    "Moved channel {ChannelId} to category",
                    channelId);
            }
            else
            {
                _logger.LogWarning(
                    "Bot reported failure moving channel {ChannelId} to category. Error: {ErrorMessage}",
                    channelId,
                    response.ErrorMessage);
            }

            return (response.Success, response.ErrorMessage);
        }
        catch (RpcException ex)
        {
            _logger.LogError(
                ex,
                "Error moving channel {ChannelId} to category",
                channelId);

            return (false, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _channel.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
