using AmazoniaApi.Bot.Grpc;
using AmazoniaApi.Core.Models;
using Discord;
using Discord.WebSocket;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Linq;

namespace AmazoniaApi.Bot.Services;

public sealed class BotGrpcService : BotService.BotServiceBase
{
    private readonly DiscordSocketClient _discordClient;
    private readonly ApiClient _apiClient;
    private readonly ChannelPermissionManager _permissionManager;
    private readonly ILogger<BotGrpcService> _logger;
    private readonly IConfiguration _configuration;

    public BotGrpcService(
        DiscordSocketClient discordClient,
        ApiClient apiClient,
        ChannelPermissionManager permissionManager,
        ILogger<BotGrpcService> logger,
        IConfiguration configuration)
    {
        _discordClient = discordClient;
        _apiClient = apiClient;
        _permissionManager = permissionManager;
        _logger = logger;
        _configuration = configuration;
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

    public override async Task<GetAllChannelMessagesResponse> GetAllChannelMessages(GetAllChannelMessagesRequest request, ServerCallContext context)
    {
        var response = new GetAllChannelMessagesResponse();

        if (!ulong.TryParse(request.ChannelId, out var channelId))
        {
            response.Success = false;
            response.ErrorMessage = "Invalid channel id.";
            return response;
        }

        if (_discordClient.GetChannel(channelId) is not IMessageChannel messageChannel)
        {
            response.Success = false;
            response.ErrorMessage = "Channel not found or bot lacks access.";
            return response;
        }

        try
        {
            _logger.LogInformation("Fetching all messages from channel {ChannelId}", channelId);

            // Fetch all messages from the channel (Discord API limit is 100 per request, so we paginate)
            var allMessages = new List<IMessage>();
            const int batchSize = 100;
            
            // Start by fetching the most recent messages
            var messages = await messageChannel.GetMessagesAsync(limit: batchSize).FlattenAsync();
            var messageList = messages.ToList();
            allMessages.AddRange(messageList);

            // Continue fetching older messages until we've got them all
            while (messageList.Count == batchSize)
            {
                var lastMessage = messageList.Last();
                messages = await messageChannel.GetMessagesAsync(lastMessage, Direction.Before, batchSize).FlattenAsync();
                messageList = messages.ToList();
                
                if (messageList.Count == 0)
                {
                    break;
                }
                
                allMessages.AddRange(messageList);
            }

            // Convert to response format
            foreach (var message in allMessages)
            {
                // Only include user messages (not bot messages)
                if (message.Source != MessageSource.User)
                {
                    continue;
                }

                var content = message.Content?.Trim() ?? string.Empty;
                
                // Handle messages with only attachments
                if (string.IsNullOrWhiteSpace(content) && message.Attachments.Count > 0)
                {
                    var attachmentSummary = string.Join(", ", message.Attachments.Select(a => $"{a.Filename}: {a.Url}"));
                    content = $"[Attachments] {attachmentSummary}";
                    if (content.Length > 2000)
                    {
                        content = content[..2000];
                    }
                }

                // Skip empty messages
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                var messageInfo = new DiscordMessageInfo
                {
                    DiscordMessageId = message.Id,
                    Content = content,
                    AuthorDiscordId = message.Author.Id,
                    Timestamp = ((DateTimeOffset)message.Timestamp).ToUnixTimeSeconds()
                };

                response.Messages.Add(messageInfo);
            }

            response.Success = true;
            _logger.LogInformation("Fetched {MessageCount} messages from channel {ChannelId}", response.Messages.Count, channelId);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch messages from channel {ChannelId}", channelId);
            response.Success = false;
            response.ErrorMessage = ex.Message;
            return response;
        }
    }

    public override async Task<CreateTicketChannelResponse> CreateTicketChannel(CreateTicketChannelRequest request, ServerCallContext context)
    {
        var response = new CreateTicketChannelResponse();

        try
        {
            _logger.LogInformation("Received request to create ticket channel for Discord user {DiscordUserId}", request.DiscordUserId);

            // Validate title length
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length < 3 || request.Title.Length > 30)
            {
                response.Success = false;
                response.ErrorMessage = "Title must be between 3 and 30 characters.";
                return response;
            }

            // Determine which guild to use
            ulong targetGuildId = request.GuildId;
            if (targetGuildId == 0)
            {
                // Use configured default guild ID if available
                var configuredGuildId = _configuration.GetValue<ulong?>("Discord:GuildId") ?? 0;
                if (configuredGuildId != 0)
                {
                    targetGuildId = configuredGuildId;
                    _logger.LogInformation("Using configured default guild ID: {GuildId}", targetGuildId);
                }
            }

            SocketGuild? guild = null;
            IGuildUser? user = null;

            if (targetGuildId != 0)
            {
                // Use specified or configured guild
                guild = _discordClient.GetGuild(targetGuildId);
                if (guild == null)
                {
                    response.Success = false;
                    response.ErrorMessage = $"Guild {targetGuildId} not found or bot is not a member.";
                    return response;
                }

                // Fetch user from cache first
                _logger.LogInformation("Fetching user {DiscordUserId} from guild {GuildId}", request.DiscordUserId, targetGuildId);
                user = guild.GetUser(request.DiscordUserId);
                
                // If not in cache, fetch via REST API
                if (user == null)
                {
                    _logger.LogInformation("User {DiscordUserId} not in cache, fetching via REST API for guild {GuildId}", request.DiscordUserId, targetGuildId);
                    try
                    {
                        user = await _discordClient.Rest.GetGuildUserAsync(targetGuildId, request.DiscordUserId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch user {DiscordUserId} via REST API", request.DiscordUserId);
                    }
                }
                
                if (user == null)
                {
                    response.Success = false;
                    response.ErrorMessage = $"User {request.DiscordUserId} not found in guild {targetGuildId}.";
                    return response;
                }
            }
            else
            {
                // Find a guild where the user is a member by checking all guilds
                _logger.LogInformation("No guild specified, searching for user {DiscordUserId} in all guilds", request.DiscordUserId);
                
                foreach (var g in _discordClient.Guilds)
                {
                    try
                    {
                        // Try to get user from cache first
                        user = g.GetUser(request.DiscordUserId);
                        
                        // If not in cache, fetch via REST API
                        if (user == null)
                        {
                            _logger.LogDebug("User {DiscordUserId} not in cache for guild {GuildId}, fetching via REST API", request.DiscordUserId, g.Id);
                            try
                            {
                                user = await _discordClient.Rest.GetGuildUserAsync(g.Id, request.DiscordUserId);
                            }
                            catch (Exception restEx)
                            {
                                _logger.LogDebug(restEx, "Failed to fetch user {DiscordUserId} via REST API for guild {GuildId}", request.DiscordUserId, g.Id);
                                // Continue to next guild
                                continue;
                            }
                        }
                        
                        if (user != null)
                        {
                            guild = g;
                            _logger.LogInformation("Found user {DiscordUserId} in guild {GuildId}", request.DiscordUserId, g.Id);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error checking guild {GuildId} for user {DiscordUserId}", g.Id, request.DiscordUserId);
                        // Continue to next guild
                    }
                }

                if (guild == null || user == null)
                {
                    response.Success = false;
                    response.ErrorMessage = $"User {request.DiscordUserId} not found in any guild where bot is a member.";
                    return response;
                }
            }

            // Generate channel name from title
            var channelName = GenerateChannelName(request.Title);

            // Get or create Active Tickets category
            var activeTicketsCategory = await _permissionManager.GetOrCreateActiveTicketsCategoryAsync(guild);
            var ticketCategory = activeTicketsCategory ?? guild.CategoryChannels
                .FirstOrDefault(c => string.Equals(c.Name, "Tickets", StringComparison.OrdinalIgnoreCase));

            // Set up permission overwrites
            var overwrites = new List<Overwrite>
            {
                new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny))
            };

            // Creator permissions
            var creatorPermissions = new OverwritePermissions(
                viewChannel: PermValue.Allow,
                sendMessages: PermValue.Allow,
                readMessageHistory: PermValue.Allow,
                addReactions: PermValue.Allow);
            overwrites.Add(new Overwrite(user.Id, PermissionTarget.User, creatorPermissions));

            // Bot permissions
            if (guild.GetUser(_discordClient.CurrentUser.Id) is IGuildUser botUser)
            {
                var botPermissions = new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    manageChannel: PermValue.Allow,
                    manageRoles: PermValue.Allow,
                    readMessageHistory: PermValue.Allow);
                overwrites.Add(new Overwrite(botUser.Id, PermissionTarget.User, botPermissions));
            }

            // Staff role permissions
            if (await _permissionManager.GetStaffRoleAsync(guild) is { } staffRole)
            {
                var staffPermissions = new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow);
                overwrites.Add(new Overwrite(staffRole.Id, PermissionTarget.Role, staffPermissions));
            }

            // Create the channel
            _logger.LogInformation("Creating ticket channel {ChannelName} in guild {GuildId}", channelName, guild.Id);
            var channel = await guild.CreateTextChannelAsync(channelName, properties =>
            {
                properties.CategoryId = ticketCategory?.Id;
                properties.Topic = $"Support ticket for {user.Mention}";
                properties.PermissionOverwrites = overwrites;
            });

            // Configure permissions
            var permissionsConfigured = await _permissionManager.SetupTicketChannelPermissionsAsync(channel, user.Id);
            if (!permissionsConfigured)
            {
                _logger.LogWarning("Failed to configure permissions for channel {ChannelId}", channel.Id);
            }

            // Send welcome message
            await channel.SendMessageAsync(
                $"{user.Mention} thanks for creating a ticket!\n" +
                "Please describe your issue in detail so our support team can assist you. " +
                "A staff member will respond as soon as possible.\n" +
                "_Note: Slow mode is enabled at 5 seconds to keep the conversation manageable._");

            response.Success = true;
            response.ChannelId = channel.Id.ToString();
            _logger.LogInformation("Successfully created ticket channel {ChannelId} for user {DiscordUserId}", channel.Id, request.DiscordUserId);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ticket channel for Discord user {DiscordUserId}", request.DiscordUserId);
            response.Success = false;
            response.ErrorMessage = ex.Message;
            return response;
        }
    }

    public override async Task<MoveTicketChannelToCategoryResponse> MoveTicketChannelToCategory(MoveTicketChannelToCategoryRequest request, ServerCallContext context)
    {
        var response = new MoveTicketChannelToCategoryResponse();

        if (!ulong.TryParse(request.ChannelId, out var channelId))
        {
            response.Success = false;
            response.ErrorMessage = "Invalid channel id.";
            return response;
        }

        if (string.IsNullOrWhiteSpace(request.Status))
        {
            response.Success = false;
            response.ErrorMessage = "Status is required.";
            return response;
        }

        try
        {
            // Parse status string to TicketStatus enum
            if (!Enum.TryParse<TicketStatus>(request.Status, ignoreCase: true, out var ticketStatus))
            {
                response.Success = false;
                response.ErrorMessage = $"Invalid status '{request.Status}'.";
                return response;
            }

            _logger.LogInformation("Received request to move channel {ChannelId} to category based on status {Status}", channelId, ticketStatus);

            var success = await _permissionManager.MoveTicketChannelToCategoryAsync(channelId, ticketStatus);
            
            response.Success = success;
            response.ErrorMessage = success ? string.Empty : "Failed to move channel to category.";
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move channel {ChannelId} to category", channelId);
            response.Success = false;
            response.ErrorMessage = ex.Message;
            return response;
        }
    }

    private static string GenerateChannelName(string title)
    {
        var sanitized = new System.Text.StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || char.IsDigit(c))
            {
                sanitized.Append(c);
            }
            else if (c is ' ' or '-' or '_')
            {
                sanitized.Append('-');
            }
        }

        var name = sanitized.ToString().Trim('-');
        if (string.IsNullOrEmpty(name))
        {
            name = "ticket";
        }

        // Discord channel names have a max length of 100 characters
        // Format: ticket-title (no timestamp needed since title should be unique enough)
        name = name.Length > 90 ? name.Substring(0, 90) : name;

        return $"ticket-{name}";
    }
}