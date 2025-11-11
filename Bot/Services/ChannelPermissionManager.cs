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

    public ChannelPermissionManager(
        DiscordSocketClient client,
        IConfiguration configuration,
        ILogger<ChannelPermissionManager> logger,
        TicketChannelCache ticketChannelCache)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
        _ticketChannelCache = ticketChannelCache;
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

            if (_ticketChannelCache.GetTicketInfo(channelId) is { } ticketInfo)
            {
                if (await channel.Guild.GetUserAsync(ticketInfo.CreatorDiscordId) is IGuildUser creatorUser)
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
                    _logger.LogWarning("Creator user {CreatorId} not found in guild {GuildId} for channel {ChannelId}", ticketInfo.CreatorDiscordId, channel.Guild.Id, channelId);
                }
            }
            else
            {
                _logger.LogWarning("Ticket info not found in cache for channel {ChannelId} when updating read-only status.", channelId);
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
}

