using AmazoniaApi.Core.Models;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AmazoniaApi.Bot.Services;

public sealed class ChannelPermissionManager
{
    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChannelPermissionManager> _logger;
    private readonly TicketChannelCache _ticketChannelCache;
    private readonly ApiClient _apiClient;

    public ChannelPermissionManager(
        DiscordSocketClient client,
        IConfiguration configuration,
        ILogger<ChannelPermissionManager> logger,
        TicketChannelCache ticketChannelCache,
        ApiClient apiClient)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
        _ticketChannelCache = ticketChannelCache;
        _apiClient = apiClient;
    }

    public async Task<bool> SetChannelReadOnlyAsync(ulong channelId, bool readOnly)
    {
        if (_client.GetChannel(channelId) is not ITextChannel channel)
        {
            _logger.LogWarning("Cannot update permissions. Channel {ChannelId} not found.", channelId);
            return false;
        }

        try
        {
            var everyoneRole = channel.Guild.EveryoneRole;
            var everyoneOverwrite = channel.GetPermissionOverwrite(everyoneRole) ?? new OverwritePermissions();

            everyoneOverwrite = readOnly
                ? everyoneOverwrite.Modify(sendMessages: PermValue.Deny, addReactions: PermValue.Deny)
                : everyoneOverwrite.Modify(sendMessages: PermValue.Inherit, addReactions: PermValue.Inherit);

            await channel.AddPermissionOverwriteAsync(everyoneRole, everyoneOverwrite);

            // Try to get ticket info from cache first, then from API if not found
            var ticketInfo = _ticketChannelCache.GetTicketInfo(channelId);
            if (ticketInfo == null)
            {
                _logger.LogInformation("Ticket info not found in cache for channel {ChannelId}, fetching from API", channelId);
                try
                {
                    ticketInfo = await _apiClient.GetTicketInfoAsync(channelId.ToString());
                    if (ticketInfo != null)
                    {
                        // Update cache with the fetched ticket info
                        _ticketChannelCache.AddOrUpdateTicket(ticketInfo);
                        _logger.LogInformation("Fetched and cached ticket info for channel {ChannelId}", channelId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch ticket info from API for channel {ChannelId}", channelId);
                }
            }

            if (ticketInfo is { } info)
            {
                if (await channel.Guild.GetUserAsync(info.CreatorDiscordId) is IGuildUser creatorUser)
                {
                    var creatorOverwrite = channel.GetPermissionOverwrite(creatorUser) ??
                        new OverwritePermissions(
                            viewChannel: PermValue.Allow,
                            sendMessages: PermValue.Allow,
                            readMessageHistory: PermValue.Allow,
                            addReactions: PermValue.Allow);

                    creatorOverwrite = creatorOverwrite.Modify(sendMessages: readOnly ? PermValue.Deny : PermValue.Allow);
                    await channel.AddPermissionOverwriteAsync(creatorUser, creatorOverwrite);
                }
                else
                {
                    _logger.LogWarning("Creator user {CreatorId} not found in guild {GuildId} for channel {ChannelId}", info.CreatorDiscordId, channel.Guild.Id, channelId);
                }
            }
            else
            {
                _logger.LogWarning("Ticket info not found in cache or API for channel {ChannelId} when updating read-only status.", channelId);
            }

            if (await GetStaffRoleAsync(channel.Guild) is { } staffRole)
            {
                var staffOverwrite = channel.GetPermissionOverwrite(staffRole) ??
                    new OverwritePermissions(
                        viewChannel: PermValue.Allow,
                        sendMessages: PermValue.Allow,
                        readMessageHistory: PermValue.Allow);

                staffOverwrite = staffOverwrite.Modify(sendMessages: readOnly ? PermValue.Deny : PermValue.Allow);
                await channel.AddPermissionOverwriteAsync(staffRole, staffOverwrite);
            }

            _logger.LogInformation("Updated read-only status for channel {ChannelId} to {ReadOnly}", channelId, readOnly);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update permissions for channel {ChannelId}", channelId);
            return false;
        }
    }

    public async Task<bool> SetupTicketChannelPermissionsAsync(ITextChannel channel, ulong creatorUserId)
    {
        try
        {
            var everyoneRole = channel.Guild.EveryoneRole;
            await channel.AddPermissionOverwriteAsync(everyoneRole, new OverwritePermissions(viewChannel: PermValue.Deny));

            var creator = await channel.Guild.GetUserAsync(creatorUserId);
            if (creator != null)
            {
                var permissions = new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    addReactions: PermValue.Allow);
                await channel.AddPermissionOverwriteAsync(creator, permissions);
            }

            var botUser = await channel.Guild.GetUserAsync(_client.CurrentUser.Id);
            if (botUser != null)
            {
                var permissions = new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    manageChannel: PermValue.Allow,
                    manageRoles: PermValue.Allow,
                    readMessageHistory: PermValue.Allow);
                await channel.AddPermissionOverwriteAsync(botUser, permissions);
            }

            var staffRole = await GetStaffRoleAsync(channel.Guild);
            if (staffRole != null)
            {
                var permissions = new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow);
                await channel.AddPermissionOverwriteAsync(staffRole, permissions);
            }

            await channel.ModifyAsync(props => props.SlowModeInterval = 5);

            _logger.LogInformation("Configured permissions for ticket channel {ChannelId}", channel.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure ticket channel permissions for channel {ChannelId}", channel.Id);
            return false;
        }
    }

    public Task<IRole?> GetStaffRoleAsync(IGuild guild)
    {
        var staffRoleId = _configuration.GetValue<ulong?>("Discord:StaffRoleId") ?? 0;
        if (staffRoleId != 0)
        {
            var role = guild.GetRole(staffRoleId);
            return Task.FromResult<IRole?>(role);
        }

        var staffRoleName = _configuration.GetValue<string>("Discord:StaffRoleName");
        if (!string.IsNullOrWhiteSpace(staffRoleName))
        {
            var role = guild.Roles.FirstOrDefault(r => string.Equals(r.Name, staffRoleName, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<IRole?>(role);
        }

        return Task.FromResult<IRole?>(null);
    }

    /// <summary>
    /// Gets or creates the "Active Tickets" category in the guild
    /// </summary>
    public async Task<ICategoryChannel?> GetOrCreateActiveTicketsCategoryAsync(IGuild guild)
    {
        try
        {
            // Cast to SocketGuild to access CategoryChannels
            if (guild is not SocketGuild socketGuild)
            {
                _logger.LogWarning("Cannot create category: guild is not a SocketGuild");
                return null;
            }

            // First, try to find existing category
            var category = socketGuild.CategoryChannels
                .FirstOrDefault(c => string.Equals(c.Name, "Active Tickets", StringComparison.OrdinalIgnoreCase));

            if (category != null)
            {
                return category;
            }

            // Category doesn't exist, create it
            _logger.LogInformation("Creating 'Active Tickets' category in guild {GuildId}", guild.Id);
            var createdCategory = await socketGuild.CreateCategoryChannelAsync("Active Tickets");
            _logger.LogInformation("Created 'Active Tickets' category {CategoryId} in guild {GuildId}", createdCategory.Id, guild.Id);
            
            // Get the SocketCategoryChannel from the guild
            category = socketGuild.GetCategoryChannel(createdCategory.Id);
            return category;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create 'Active Tickets' category in guild {GuildId}", guild.Id);
            return null;
        }
    }

    /// <summary>
    /// Gets or creates the "Closed Tickets" category in the guild
    /// </summary>
    public async Task<ICategoryChannel?> GetOrCreateClosedTicketsCategoryAsync(IGuild guild)
    {
        try
        {
            // Cast to SocketGuild to access CategoryChannels
            if (guild is not SocketGuild socketGuild)
            {
                _logger.LogWarning("Cannot create category: guild is not a SocketGuild");
                return null;
            }

            // First, try to find existing category
            var category = socketGuild.CategoryChannels
                .FirstOrDefault(c => string.Equals(c.Name, "Closed Tickets", StringComparison.OrdinalIgnoreCase));

            if (category != null)
            {
                return category;
            }

            // Category doesn't exist, create it
            _logger.LogInformation("Creating 'Closed Tickets' category in guild {GuildId}", guild.Id);
            var createdCategory = await socketGuild.CreateCategoryChannelAsync("Closed Tickets");
            _logger.LogInformation("Created 'Closed Tickets' category {CategoryId} in guild {GuildId}", createdCategory.Id, guild.Id);
            
            // Get the SocketCategoryChannel from the guild
            category = socketGuild.GetCategoryChannel(createdCategory.Id);
            return category;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create 'Closed Tickets' category in guild {GuildId}", guild.Id);
            return null;
        }
    }

    /// <summary>
    /// Moves a ticket channel to the appropriate category based on its status
    /// </summary>
    public async Task<bool> MoveTicketChannelToCategoryAsync(ulong channelId, TicketStatus status)
    {
        _logger.LogInformation("MoveTicketChannelToCategoryAsync called: ChannelId={ChannelId}, Status={Status}", channelId, status);
        
        ITextChannel? channel = _client.GetChannel(channelId) as ITextChannel;
        
        // If not in cache, try to fetch via REST API
        if (channel == null)
        {
            _logger.LogInformation("Channel {ChannelId} not in cache, fetching via REST API", channelId);
            try
            {
                var restChannel = await _client.Rest.GetChannelAsync(channelId);
                if (restChannel is ITextChannel textChannel)
                {
                    channel = textChannel;
                    _logger.LogInformation("Successfully fetched channel {ChannelId} via REST API", channelId);
                }
                else
                {
                    _logger.LogWarning("Channel {ChannelId} is not a text channel", channelId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch channel {ChannelId} via REST API: {Message}", channelId, ex.Message);
                return false;
            }
        }

        if (channel == null)
        {
            _logger.LogWarning("Cannot move channel. Channel {ChannelId} not found.", channelId);
            return false;
        }

        // Get the guild - if channel is from REST API, we need to get the guild differently
        IGuild guild;
        if (channel is SocketTextChannel socketChannel)
        {
            guild = socketChannel.Guild;
        }
        else
        {
            // For REST channels, we need to find the guild
            var guildId = channel.GuildId;
            
            // Try cache first, then REST API
            var socketGuild = _client.GetGuild(guildId);
            if (socketGuild != null)
            {
                guild = socketGuild;
            }
            else
            {
                guild = await _client.Rest.GetGuildAsync(guildId);
                if (guild == null)
                {
                    _logger.LogWarning("Cannot find guild {GuildId} for channel {ChannelId}", guildId, channelId);
                    return false;
                }
            }
        }

        _logger.LogInformation("Found channel {ChannelId} ({ChannelName}) in guild {GuildId}", channelId, channel.Name, guild.Id);

        try
        {
            ICategoryChannel? targetCategory = null;

            // Determine which category based on status
            if (status is TicketStatus.Closed or TicketStatus.Archived or TicketStatus.Completed)
            {
                _logger.LogInformation("Status {Status} requires Closed Tickets category", status);
                targetCategory = await GetOrCreateClosedTicketsCategoryAsync(guild);
            }
            else
            {
                // All other statuses go to Active Tickets
                _logger.LogInformation("Status {Status} requires Active Tickets category", status);
                targetCategory = await GetOrCreateActiveTicketsCategoryAsync(guild);
            }

            if (targetCategory == null)
            {
                _logger.LogWarning("Failed to get or create target category for channel {ChannelId}", channelId);
                return false;
            }

            _logger.LogInformation("Target category: {CategoryName} (ID: {CategoryId}), Current channel category: {CurrentCategoryId}", 
                targetCategory.Name, targetCategory.Id, channel.CategoryId);

            // Only move if it's in a different category
            if (channel.CategoryId != targetCategory.Id)
            {
                _logger.LogInformation("Moving channel {ChannelId} from category {FromCategoryId} to {ToCategoryId}", 
                    channelId, channel.CategoryId ?? 0, targetCategory.Id);
                
                // Use REST API to modify if channel is from REST API
                if (channel is SocketTextChannel socketTextChannel)
                {
                    await socketTextChannel.ModifyAsync(props =>
                    {
                        props.CategoryId = targetCategory.Id;
                    });
                }
                else
                {
                    // For REST channels, use REST API
                    await channel.ModifyAsync(props =>
                    {
                        props.CategoryId = targetCategory.Id;
                    });
                }
                
                _logger.LogInformation("Successfully moved channel {ChannelId} to category {CategoryName} (Status: {Status})", 
                    channelId, targetCategory.Name, status);
                return true;
            }
            else
            {
                _logger.LogInformation("Channel {ChannelId} is already in the correct category {CategoryName}", 
                    channelId, targetCategory.Name);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move channel {ChannelId} to category: {Message}, StackTrace: {StackTrace}", 
                channelId, ex.Message, ex.StackTrace);
            return false;
        }
    }
}

