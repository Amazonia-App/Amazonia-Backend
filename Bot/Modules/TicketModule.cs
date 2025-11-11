using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AmazoniaApi.Bot.Services;
using AmazoniaApi.Core.Models;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Discord.Net;
using Microsoft.Extensions.Logging;

namespace AmazoniaApi.Bot.Modules;

public sealed class TicketModule : InteractionModuleBase<SocketInteractionContext>
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private readonly ApiClient _apiClient;
    private readonly TicketChannelCache _ticketChannelCache;
    private readonly ChannelPermissionManager _permissionManager;
    private readonly ILogger<TicketModule> _logger;
#pragma warning restore CS8618
    
    // Track interactions currently being processed to prevent duplicate execution
    private static readonly HashSet<ulong> _processingInteractions = new();
    private static readonly object _processingInteractionsLock = new();

    public TicketModule(
        ApiClient apiClient,
        TicketChannelCache ticketChannelCache,
        ChannelPermissionManager permissionManager,
        ILogger<TicketModule> logger)
    {
        // Validate and assign - ThrowIfNull ensures these are non-null
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(ticketChannelCache);
        ArgumentNullException.ThrowIfNull(permissionManager);
        ArgumentNullException.ThrowIfNull(logger);
        
        // Use null-forgiving operator since ThrowIfNull guarantees non-null
        _apiClient = apiClient!;
        _ticketChannelCache = ticketChannelCache!;
        _permissionManager = permissionManager!;
        _logger = logger!;

        _logger.LogInformation(
            "TicketModule constructor called. Dependencies: ApiClient=injected, TicketChannelCache=injected, ChannelPermissionManager=injected");
    }

    [SlashCommand("open-ticket", "Create a new support ticket")]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task CreateTicketAsync([Summary(description: "Title for your ticket")] string title)
    {
        // Log immediately at method entry - before any try/catch
        _logger.LogInformation("=== CreateTicketAsync METHOD ENTRY ===");
        
        await CreateTicketInternalAsync(Context, title);
    }

    /// <summary>
    /// Internal method that can be called directly with a context, bypassing InteractionService issues
    /// </summary>
    public async Task CreateTicketInternalAsync(SocketInteractionContext context, string title)
    {
        // Check if this interaction is already being processed
        bool alreadyProcessing = false;
        lock (_processingInteractionsLock)
        {
            if (_processingInteractions.Contains(context.Interaction.Id))
            {
                alreadyProcessing = true;
                _logger.LogWarning("CreateTicketInternalAsync already processing interaction {InteractionId}, skipping duplicate execution.", context.Interaction.Id);
            }
            else
            {
                _processingInteractions.Add(context.Interaction.Id);
            }
        }
        
        if (alreadyProcessing)
        {
            return;
        }
        
        try
        {
            _logger.LogInformation("CreateTicketInternalAsync invoked by user {UserId} in guild {GuildId}", context.User.Id, context.Guild?.Id ?? 0);

            // CRITICAL: Defer immediately to prevent Discord timeout (3 seconds)
            // This must happen synchronously before any other async operations
            if (!context.Interaction.HasResponded)
            {
                _logger.LogInformation("Attempting to defer interaction for user {UserId}", context.User.Id);
                try
                {
                    await context.Interaction.DeferAsync(ephemeral: true);
                    _logger.LogInformation("CreateTicket deferred response sent for user {UserId}", context.User.Id);
                }
                catch (HttpException httpEx) when (httpEx.HttpCode == System.Net.HttpStatusCode.BadRequest)
                {
                    // Interaction already acknowledged (error 40060) - this can happen if there's a race condition
                    _logger.LogWarning("Interaction already acknowledged for user {UserId} (HTTP {StatusCode}). Will try to modify existing response.", context.User.Id, httpEx.HttpCode);
                    // Continue execution - we'll try to modify the response later
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to defer interaction for user {UserId}. Exception: {ExceptionType}, Message: {Message}", context.User.Id, ex.GetType().Name, ex.Message);
                    try
                    {
                        if (!context.Interaction.HasResponded)
                        {
                            await context.Interaction.RespondAsync("I couldn't acknowledge your request in time. Please try again in a moment.", ephemeral: true);
                        }
                    }
                    catch (Exception respondEx)
                    {
                        _logger.LogError(respondEx, "Failed to send fallback response after defer failure for user {UserId}", context.User.Id);
                    }
                    return;
                }
            }
            else
            {
                _logger.LogWarning("CreateTicket interaction already responded to for user {UserId}. Continuing execution.", context.User.Id);
            }

            if (context.Guild is null)
            {
                try
                {
                    if (context.Interaction is SocketSlashCommand slashCmd)
                    {
                        await slashCmd.ModifyOriginalResponseAsync(props =>
                        {
                            props.Content = "This command can only be used in a server.";
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send guild-only error message for user {UserId}", context.User.Id);
                }
                return;
            }

            // Validate title length
            if (string.IsNullOrWhiteSpace(title) || title.Length < 3 || title.Length > 30)
            {
                try
                {
                    if (context.Interaction is SocketSlashCommand slashCmd)
                    {
                        await slashCmd.ModifyOriginalResponseAsync(props =>
                        {
                            props.Content = "Title must be between 3 and 30 characters.";
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send title validation error message for user {UserId}", context.User.Id);
                }
                return;
            }

            var channelName = GenerateChannelName(title);
            ITextChannel? channel = null;

            try
            {
                _logger.LogInformation("Starting ticket creation process for user {UserId}", context.User.Id);

                // Get or create Active Tickets category
                var activeTicketsCategory = await _permissionManager.GetOrCreateActiveTicketsCategoryAsync(context.Guild);
                var ticketCategory = activeTicketsCategory ?? context.Guild.CategoryChannels
                    .FirstOrDefault(c => string.Equals(c.Name, "Tickets", StringComparison.OrdinalIgnoreCase));

                var overwrites = new List<Overwrite>
                {
                    new(context.Guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny))
                };

                if (context.Guild.GetUser(context.User.Id) is IGuildUser creatorUser)
                {
                    var creatorPermissions = new OverwritePermissions(
                        viewChannel: PermValue.Allow,
                        sendMessages: PermValue.Allow,
                        readMessageHistory: PermValue.Allow,
                        addReactions: PermValue.Allow);
                    overwrites.Add(new Overwrite(creatorUser.Id, PermissionTarget.User, creatorPermissions));
                    _logger.LogInformation("Creator user {UserId} found in guild {GuildId}", creatorUser.Id, context.Guild.Id);
                }
                else
                {
                    _logger.LogWarning("Creator user {UserId} not found in guild {GuildId} while creating ticket.", context.User.Id, context.Guild.Id);
                }

                if (context.Guild.GetUser(context.Client.CurrentUser.Id) is IGuildUser botUser)
                {
                    var botPermissions = new OverwritePermissions(
                        viewChannel: PermValue.Allow,
                        sendMessages: PermValue.Allow,
                        manageChannel: PermValue.Allow,
                        manageRoles: PermValue.Allow,
                        readMessageHistory: PermValue.Allow);
                    overwrites.Add(new Overwrite(botUser.Id, PermissionTarget.User, botPermissions));
                }
                else
                {
                    _logger.LogWarning("Bot user not found in guild {GuildId} while creating ticket.", context.Guild.Id);
                }

                if (await _permissionManager.GetStaffRoleAsync(context.Guild) is { } staffRole)
                {
                    var staffPermissions = new OverwritePermissions(
                        viewChannel: PermValue.Allow,
                        sendMessages: PermValue.Allow,
                        readMessageHistory: PermValue.Allow);
                    overwrites.Add(new Overwrite(staffRole.Id, PermissionTarget.Role, staffPermissions));
                }

                _logger.LogInformation("Creating Discord channel {ChannelName}", channelName);
                channel = await context.Guild.CreateTextChannelAsync(channelName, properties =>
                {
                    properties.CategoryId = ticketCategory?.Id;
                    properties.Topic = $"Support ticket for {context.User.Mention}";
                    properties.PermissionOverwrites = overwrites;
                });
                _logger.LogInformation("Created ticket channel {ChannelId}", channel.Id);

                var permissionsConfigured = await _permissionManager.SetupTicketChannelPermissionsAsync(channel, context.User.Id);
                if (!permissionsConfigured)
                {
                    _logger.LogWarning("Failed to configure permissions for channel {ChannelId}", channel.Id);
                }
                else
                {
                    _logger.LogInformation("Permissions configured for channel {ChannelId}", channel.Id);
                }

                _logger.LogInformation("Calling API to create ticket for channel {ChannelId}", channel.Id);
                await _apiClient.CreateTicketAsync(channel.Id.ToString(), context.User.Id, title);
                _ticketChannelCache.AddTicket(channel.Id, context.User.Id, TicketStatus.Created);
                _logger.LogInformation("Ticket persisted via API for channel {ChannelId}", channel.Id);

                _logger.LogInformation("Sending welcome message to channel {ChannelId}", channel.Id);
                await channel.SendMessageAsync(
                    $"{context.User.Mention} thanks for creating a ticket!\n" +
                    "Please describe your issue in detail so our support team can assist you. " +
                    "A staff member will respond as soon as possible.\n" +
                    "_Note: Slow mode is enabled at 5 seconds to keep the conversation manageable._");

                _logger.LogInformation("Updating interaction response for channel {ChannelId}", channel.Id);
                if (context.Interaction is SocketSlashCommand finalSlashCmd)
                {
                    await finalSlashCmd.ModifyOriginalResponseAsync(props =>
                    {
                        props.Content = $"Ticket created successfully! {channel.Mention}";
                    });
                }
                _logger.LogInformation("CreateTicket completed successfully for channel {ChannelId}", channel.Id);
            }
            catch (HttpException httpEx)
            {
                _logger.LogError(httpEx, "Discord API error while creating ticket channel. Status: {StatusCode}, Reason: {Reason}", httpEx.HttpCode, httpEx.Reason);
                try
                {
                    if (channel is not null)
                    {
                        try
                        {
                            await channel.DeleteAsync();
                            _logger.LogInformation("Deleted channel {ChannelId} after error", channel.Id);
                        }
                        catch (Exception deleteEx)
                        {
                            _logger.LogError(deleteEx, "Failed to delete channel {ChannelId} after error", channel.Id);
                        }
                    }

                    if (context.Interaction is SocketSlashCommand errorSlashCmd)
                    {
                        await errorSlashCmd.ModifyOriginalResponseAsync(props =>
                        {
                            props.Content = "I couldn't create the ticket channel due to a permissions issue or rate limit. Please contact an administrator.";
                        });
                    }
                }
                catch (Exception modifyEx)
                {
                    _logger.LogError(modifyEx, "Failed to send error response after HttpException");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating ticket. Exception type: {ExceptionType}, Message: {Message}, StackTrace: {StackTrace}", ex.GetType().Name, ex.Message, ex.StackTrace);
                try
                {
                    if (channel is not null)
                    {
                        try
                        {
                            await channel.DeleteAsync();
                            _logger.LogInformation("Deleted channel {ChannelId} after unexpected error", channel.Id);
                        }
                        catch (Exception deleteEx)
                        {
                            _logger.LogError(deleteEx, "Failed to delete channel {ChannelId} after unexpected error", channel.Id);
                        }
                    }

                    if (context.Interaction.HasResponded && context.Interaction is SocketSlashCommand errorSlashCmd)
                    {
                        await errorSlashCmd.ModifyOriginalResponseAsync(props =>
                        {
                            props.Content = "Something went wrong while creating your ticket. Please try again later.";
                        });
                    }
                    else if (!context.Interaction.HasResponded && context.Interaction is SocketSlashCommand respondSlashCmd)
                    {
                        await respondSlashCmd.RespondAsync("Something went wrong while creating your ticket. Please try again later.", ephemeral: true);
                    }
                }
                catch (Exception modifyEx)
                {
                    _logger.LogError(modifyEx, "Failed to send error response after unexpected exception. ModifyEx: {ExceptionType}, {Message}", modifyEx.GetType().Name, modifyEx.Message);
                }
            }
        }
        catch (Exception outerEx)
        {
            _logger.LogCritical(outerEx, "CRITICAL: Unhandled exception in CreateTicketInternalAsync. Type: {ExceptionType}, Message: {Message}, StackTrace: {StackTrace}", outerEx.GetType().Name, outerEx.Message, outerEx.StackTrace);
            try
            {
                if (!context.Interaction.HasResponded && context.Interaction is SocketSlashCommand criticalSlashCmd)
                {
                    await criticalSlashCmd.RespondAsync("A critical error occurred. Please contact an administrator.", ephemeral: true);
                }
                else if (context.Interaction.HasResponded && context.Interaction is SocketSlashCommand criticalModifyCmd)
                {
                    await criticalModifyCmd.ModifyOriginalResponseAsync(props =>
                    {
                        props.Content = "A critical error occurred. Please contact an administrator.";
                    });
                }
            }
            catch
            {
                // Last resort - can't respond
                _logger.LogCritical("Could not send any response to user after critical error");
            }
        }
        finally
        {
            // Remove from processing set when done
            lock (_processingInteractionsLock)
            {
                _processingInteractions.Remove(context.Interaction.Id);
            }
        }
    }

    [SlashCommand("update-status", "Update the status of this ticket")]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task UpdateStatusAsync(
        [Summary(description: "The new status for this ticket"), Autocomplete(typeof(StatusAutocompleteHandler))] string status)
    {
        // Defer immediately to prevent Discord timeout
        if (!Context.Interaction.HasResponded)
        {
            await Context.Interaction.DeferAsync(ephemeral: true);
        }
        
        await UpdateStatusInternalAsync(Context, status);
    }

    private async Task UpdateStatusInternalAsync(SocketInteractionContext context, string status)
    {
        // Check if this interaction is already being processed
        bool alreadyProcessing = false;
        lock (_processingInteractionsLock)
        {
            if (_processingInteractions.Contains(context.Interaction.Id))
            {
                alreadyProcessing = true;
                _logger.LogWarning("UpdateStatusInternalAsync already processing interaction {InteractionId}, skipping duplicate execution.", context.Interaction.Id);
            }
            else
            {
                _processingInteractions.Add(context.Interaction.Id);
            }
        }

        if (alreadyProcessing)
        {
            return;
        }

        try
        {
            _logger.LogInformation("UpdateStatusInternalAsync invoked by user {UserId} in channel {ChannelId}", context.User.Id, context.Channel?.Id ?? 0);

            // Note: Defer should already be done in UpdateStatusAsync, but check just in case
            if (!context.Interaction.HasResponded)
            {
                _logger.LogWarning("UpdateStatusInternalAsync: Interaction not deferred, deferring now");
                try
                {
                    await context.Interaction.DeferAsync(ephemeral: true);
                    _logger.LogInformation("UpdateStatus deferred response sent for user {UserId}", context.User.Id);
                }
                catch (HttpException httpEx) when (httpEx.HttpCode == System.Net.HttpStatusCode.BadRequest)
                {
                    _logger.LogWarning("Interaction already acknowledged for user {UserId} (HTTP {StatusCode}).", context.User.Id, httpEx.HttpCode);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to defer interaction for user {UserId}", context.User.Id);
                    try
                    {
                        if (!context.Interaction.HasResponded)
                        {
                            await context.Interaction.RespondAsync("I couldn't acknowledge your request in time. Please try again in a moment.", ephemeral: true);
                        }
                    }
                    catch (Exception respondEx)
                    {
                        _logger.LogError(respondEx, "Failed to send fallback response after defer failure for user {UserId}", context.User.Id);
                    }
                    return;
                }
            }

            if (context.Guild is null)
            {
                try
                {
                    if (context.Interaction is SocketSlashCommand slashCmd)
                    {
                        await slashCmd.ModifyOriginalResponseAsync(props =>
                        {
                            props.Content = "This command can only be used in a server.";
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send guild-only error message for user {UserId}", context.User.Id);
                }
                return;
            }

            // Check if this is a ticket channel
            var channelId = context.Channel?.Id ?? 0;
            if (channelId == 0)
            {
                try
                {
                    if (context.Interaction is SocketSlashCommand slashCmd)
                    {
                        await slashCmd.ModifyOriginalResponseAsync(props =>
                        {
                            props.Content = "Could not determine the channel. Please try again.";
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send channel error message for user {UserId}", context.User.Id);
                }
                return;
            }

            // Check if channel is a ticket channel
            var isTicketChannel = _ticketChannelCache.IsTicketChannel(channelId);
            if (!isTicketChannel)
            {
                // Try to fetch from API as fallback
                var ticketInfo = await _apiClient.GetTicketInfoAsync(channelId.ToString());
                if (ticketInfo == null)
                {
                    try
                    {
                        if (context.Interaction is SocketSlashCommand slashCmd)
                        {
                            await slashCmd.ModifyOriginalResponseAsync(props =>
                            {
                                props.Content = "This command can only be used in ticket channels.";
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send ticket-only error message for user {UserId}", context.User.Id);
                    }
                    return;
                }
                // Update cache with ticket info
                _ticketChannelCache.AddOrUpdateTicket(ticketInfo);
            }

            // Parse status
            if (!Enum.TryParse<TicketStatus>(status, ignoreCase: true, out var ticketStatus))
            {
                try
                {
                    if (context.Interaction is SocketSlashCommand slashCmd)
                    {
                        await slashCmd.ModifyOriginalResponseAsync(props =>
                        {
                            props.Content = $"Invalid status '{status}'. Please select a valid status from the autocomplete options.";
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send invalid status error message for user {UserId}", context.User.Id);
                }
                return;
            }

            // Update ticket status via API
            try
            {
                await _apiClient.UpdateTicketStatusAsync(channelId.ToString(), ticketStatus);
                _ticketChannelCache.UpdateTicketStatus(channelId, ticketStatus);

                // Note: Channel category movement is handled by the server via gRPC after status update
                // But we'll also try locally as a backup
                try
                {
                    var moveSuccess = await _permissionManager.MoveTicketChannelToCategoryAsync(channelId, ticketStatus);
                    if (!moveSuccess)
                    {
                        _logger.LogWarning("Failed to move channel {ChannelId} to category locally, but server will handle it via gRPC", channelId);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully moved channel {ChannelId} to category locally", channelId);
                    }
                }
                catch (Exception moveEx)
                {
                    _logger.LogWarning(moveEx, "Exception while moving channel {ChannelId} to category locally (server will handle via gRPC): {Message}", channelId, moveEx.Message);
                }

                if (context.Interaction is SocketSlashCommand successSlashCmd)
                {
                    await successSlashCmd.ModifyOriginalResponseAsync(props =>
                    {
                        props.Content = $"✅ Ticket status updated to **{ticketStatus}**.";
                    });
                }

                _logger.LogInformation("Ticket status updated successfully: ChannelId={ChannelId}, Status={Status}, UserId={UserId}", channelId, ticketStatus, context.User.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update ticket status for channel {ChannelId}", channelId);
                try
                {
                    if (context.Interaction is SocketSlashCommand errorSlashCmd)
                    {
                        await errorSlashCmd.ModifyOriginalResponseAsync(props =>
                        {
                            props.Content = "Failed to update ticket status. Please try again later.";
                        });
                    }
                }
                catch (Exception modifyEx)
                {
                    _logger.LogError(modifyEx, "Failed to send error response after status update failure for user {UserId}", context.User.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while updating ticket status. Exception type: {ExceptionType}, Message: {Message}", ex.GetType().Name, ex.Message);
            try
            {
                if (context.Interaction.HasResponded && context.Interaction is SocketSlashCommand errorSlashCmd)
                {
                    await errorSlashCmd.ModifyOriginalResponseAsync(props =>
                    {
                        props.Content = "Something went wrong while updating the ticket status. Please try again later.";
                    });
                }
                else if (!context.Interaction.HasResponded && context.Interaction is SocketSlashCommand respondSlashCmd)
                {
                    await respondSlashCmd.RespondAsync("Something went wrong while updating the ticket status. Please try again later.", ephemeral: true);
                }
            }
            catch (Exception modifyEx)
            {
                _logger.LogError(modifyEx, "Failed to send error response after unexpected exception");
            }
        }
        finally
        {
            // Remove from processing set when done
            lock (_processingInteractionsLock)
            {
                _processingInteractions.Remove(context.Interaction.Id);
            }
        }
    }


    private static string GenerateChannelName(string title)
    {
        var sanitized = new StringBuilder();
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

