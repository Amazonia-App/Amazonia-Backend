using AmazoniaApi.Core.Interfaces;
using AmazoniaApi.Core.Models;
using AmazoniaApi.Server.Attributes;
using AmazoniaApi.Server.DBContext;
using AmazoniaApi.Server.DTOModels;
using AmazoniaApi.Server.DTOModels.Bot;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System;

namespace AmazoniaApi.Server.Controllers;

[Authorize(Roles = Roles.Admin)]
[Route("api/[controller]")]
public class TicketController(AppDbContext context, IHelperMethods helperMethods, ILogger<TicketController> logger, IBotGrpcClient botGrpcClient) : ControllerBase
{
    //fetch messages from the bot
    [HttpPost]
    public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var existingTicket = await context.Tickets.FindAsync(request.ChannelId);
        if (existingTicket != null)
        {
            return Conflict(new {message = "Ticket already exists for this channel."});
        }

        var loggedInUser = await helperMethods.GetLoggedInUserAsync();
        var ticket = new Ticket
        {
            ChannelId = request.ChannelId,
            Status = TicketStatus.Created,
            CreatorId = loggedInUser.Id
        };

        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();

        logger.LogInformation("Ticket created: ChannelId={ChannelId}, CreatorId={CreatorId}", request.ChannelId, ticket.CreatorId);

        return Ok(ticket);
    }

    /// <summary>
    /// Public endpoint to create a ticket. Requires user authentication (Discord OAuth).
    /// Creates a Discord channel via the bot and then creates the ticket.
    /// </summary>
    [Authorize] // Override class-level Admin requirement, allow any authenticated user
    [HttpPost("CreateTicket")]
    public async Task<IActionResult> CreateTicketPublic([FromBody] CreateTicketPublicRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var loggedInUser = await helperMethods.GetLoggedInUserAsync();
        
        if (loggedInUser.DiscordId == 0)
        {
            return BadRequest(new { message = "User must have a Discord ID. Please authenticate with Discord." });
        }

        // Request bot to create the Discord channel
        var cancellationToken = HttpContext.RequestAborted;
        var (success, channelId, errorMessage) = await botGrpcClient.CreateTicketChannelAsync(loggedInUser.DiscordId, request.Title, cancellationToken);
        
        if (!success || string.IsNullOrEmpty(channelId))
        {
            logger.LogError(
                "Failed to create ticket channel for Discord user {DiscordUserId}. Error: {ErrorMessage}",
                loggedInUser.DiscordId,
                errorMessage);
            return StatusCode(500, new { message = $"Failed to create ticket channel: {errorMessage ?? "Unknown error"}" });
        }

        // Check if ticket already exists for this channel (shouldn't happen, but safety check)
        var existingTicket = await context.Tickets.FindAsync(channelId);
        if (existingTicket != null)
        {
            logger.LogWarning(
                "Ticket already exists for channel {ChannelId} created for Discord user {DiscordUserId}",
                channelId,
                loggedInUser.DiscordId);
            return Conflict(new { message = "Ticket already exists for this channel." });
        }

        // Create ticket with the channel ID returned from bot
        var ticket = new Ticket
        {
            ChannelId = channelId,
            Status = TicketStatus.Created,
            CreatorId = loggedInUser.Id,
            Title = request.Title
        };

        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Public ticket created: ChannelId={ChannelId}, CreatorId={CreatorId}, DiscordUserId={DiscordUserId}",
            channelId,
            loggedInUser.Id,
            loggedInUser.DiscordId);

        return Ok(ticket);
    }

    [AllowAnonymous]
    [ApiKeyAuth]
    [HttpPost("Bot/CreateTicket")]
    public async Task<IActionResult> CreateTicketFromBot([FromBody] BotCreateTicketRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var appUser = await context.Users.FirstOrDefaultAsync(u => u.DiscordId == request.DiscordUserId);
        if (appUser == null)
        {
            return NotFound(new { message = "User not found. User must authenticate with Discord first." });
        }

        // Request bot to create the Discord channel
        var cancellationToken = HttpContext.RequestAborted;
        var (success, channelId, errorMessage) = await botGrpcClient.CreateTicketChannelAsync(request.DiscordUserId, request.Title, cancellationToken);
        
        if (!success || string.IsNullOrEmpty(channelId))
        {
            logger.LogError(
                "Failed to create ticket channel for Discord user {DiscordUserId}. Error: {ErrorMessage}",
                request.DiscordUserId,
                errorMessage);
            return StatusCode(500, new { message = $"Failed to create ticket channel: {errorMessage ?? "Unknown error"}" });
        }

        // Check if ticket already exists for this channel (shouldn't happen, but safety check)
        var existingTicket = await context.Tickets.FindAsync(channelId);
        if (existingTicket != null)
        {
            logger.LogWarning(
                "Ticket already exists for channel {ChannelId} created for Discord user {DiscordUserId}",
                channelId,
                request.DiscordUserId);
            return Conflict(new { message = "Ticket already exists for this channel." });
        }

        // Create ticket with the channel ID returned from bot
        var ticket = new Ticket
        {
            ChannelId = channelId,
            Status = TicketStatus.Created,
            CreatorId = appUser.Id,
            Title = request.Title
        };

        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Bot created ticket: ChannelId={ChannelId}, CreatorDiscordId={CreatorDiscordId}",
            channelId,
            request.DiscordUserId);

        return Ok(ticket);
    }

    
    //send messages to a ticket channel
    [HttpPost("AddMessage")]
    public async Task<IActionResult> AddMessageToTicket([FromBody] AddMessageRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var ticket = await context.Tickets
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.ChannelId == request.ChannelId);
            
        if (ticket == null)
        {
            return NotFound("Ticket not found.");
        }

        var loggedInUser = await helperMethods.GetLoggedInUserAsync();
        var message = new Message
        {
            Content = request.Content,
            SenderId = loggedInUser.Id,
            Timestamp = DateTime.UtcNow
        };

        ticket.Messages.Add(message);
        await context.SaveChangesAsync();

        logger.LogInformation("Message added to ticket: ChannelId={ChannelId}, SenderId={SenderId}", request.ChannelId, message.SenderId);

        var cancellationToken = HttpContext.RequestAborted;
        var (success, discordMessageId, errorMessage) =
            await botGrpcClient.SendMessageToChannelAsync(request.ChannelId, request.Content, message.Id, cancellationToken);

        if (success)
        {
            logger.LogInformation(
                "Message sent to Discord: ChannelId={ChannelId}, MessageId={MessageId}, DiscordMessageId={DiscordMessageId}",
                request.ChannelId,
                message.Id,
                discordMessageId);
        }
        else
        {
            logger.LogWarning(
                "Failed to send message to Discord: ChannelId={ChannelId}, MessageId={MessageId}, Error={ErrorMessage}",
                request.ChannelId,
                message.Id,
                errorMessage);
        }

        return Ok(message);
    }

    // Get all messages for a ticket by channelId
    [Authorize] // Override class-level Admin requirement, allow any authenticated user
    [HttpGet("GetMessages/{channelId}")]
    public async Task<IActionResult> GetTicketMessages(string channelId)
    {
        if (string.IsNullOrEmpty(channelId))
        {
            return BadRequest(new { message = "ChannelId is required." });
        }

        var ticket = await context.Tickets
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.ChannelId == channelId);

        if (ticket == null)
        {
            return NotFound(new { message = "Ticket not found." });
        }

        // Get the logged-in user
        var loggedInUser = await helperMethods.GetLoggedInUserAsync();
        
        // Check if user is admin or the ticket creator
        var isAdmin = User.IsInRole(Roles.Admin);
        var isCreator = ticket.CreatorId == loggedInUser.Id;

        if (!isAdmin && !isCreator)
        {
            return StatusCode(403, new { message = "You do not have permission to view messages for this ticket." });
        }

        // Order messages by timestamp (oldest first)
        var messages = ticket.Messages
            .OrderBy(m => m.Timestamp)
            .ToList();

        logger.LogInformation(
            "User {UserId} fetched {MessageCount} messages for ticket {ChannelId}",
            loggedInUser.Id,
            messages.Count,
            channelId);

        return Ok(messages);
    }

    [AllowAnonymous]
    [ApiKeyAuth]
    [HttpPost("Bot/StoreMessage")]
    public async Task<IActionResult> StoreMessageFromBot([FromBody] BotStoreMessageRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var ticket = await context.Tickets
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.ChannelId == request.ChannelId);

        if (ticket == null)
        {
            return NotFound(new { message = "Ticket not found." });
        }

        var appUser = await context.Users.FirstOrDefaultAsync(u => u.DiscordId == request.SenderDiscordId);
        if (appUser == null)
        {
            return NotFound(new { message = "Sender not found. User must authenticate with Discord first." });
        }

        var message = new Message
        {
            Content = request.Content,
            SenderId = appUser.Id,
            DiscordMessageId = request.DiscordMessageId,
            Timestamp = DateTime.UtcNow
        };

        ticket.Messages.Add(message);
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Bot stored message {DiscordMessageId} for channel {ChannelId}",
            request.DiscordMessageId,
            request.ChannelId);

        return Ok(message);
    }
    
    //update the ticket status (open/closed)
    [HttpPut("UpdateStatus")]
    public async Task<IActionResult> UpdateTicketStatus([FromBody] UpdateTicketStatusRequest request) 
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var ticket = await context.Tickets
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.ChannelId == request.ChannelId);
        if (ticket == null)
        {
            return NotFound("Ticket not found.");
        }

        var previousStatus = ticket.Status;
        ticket.Status = request.Status;
        await context.SaveChangesAsync();

        logger.LogInformation("Ticket status updated: ChannelId={ChannelId}, Status={Status}", request.ChannelId, request.Status);

        // Send status change notification message
        var loggedInUser = await helperMethods.GetLoggedInUserAsync();
        var statusChangeMessage = new Message
        {
            Content = $"📋 **Ticket status changed:** {previousStatus} → {request.Status}",
            SenderId = loggedInUser.Id,
            Timestamp = DateTime.UtcNow
        };

        ticket.Messages.Add(statusChangeMessage);
        await context.SaveChangesAsync();

        var cancellationToken = HttpContext.RequestAborted;
        var (success, discordMessageId, errorMessage) =
            await botGrpcClient.SendMessageToChannelAsync(request.ChannelId, statusChangeMessage.Content, statusChangeMessage.Id, cancellationToken);

        if (success)
        {
            logger.LogInformation(
                "Status change message sent to Discord: ChannelId={ChannelId}, MessageId={MessageId}, DiscordMessageId={DiscordMessageId}",
                request.ChannelId,
                statusChangeMessage.Id,
                discordMessageId);
        }
        else
        {
            logger.LogWarning(
                "Failed to send status change message to Discord: ChannelId={ChannelId}, MessageId={MessageId}, Error={ErrorMessage}",
                request.ChannelId,
                statusChangeMessage.Id,
                errorMessage);
        }

        // Determine if channel permissions need to be updated
        // Closed, Archived, and Completed statuses make channels read-only
        // When reverting from these statuses to others, the channel should become writable again
        var wasReadOnly = previousStatus is TicketStatus.Completed or TicketStatus.Archived or TicketStatus.Closed;
        var shouldBeReadOnly = request.Status is TicketStatus.Completed or TicketStatus.Archived or TicketStatus.Closed;

        if (wasReadOnly != shouldBeReadOnly)
        {
            // Update channel permissions: set read-only if moving to Closed/Archived/Completed,
            // or make writable if reverting from those statuses to any other status
            var (permissionSuccess, permissionErrorMessage) = await botGrpcClient.UpdateChannelPermissionsAsync(request.ChannelId, shouldBeReadOnly, cancellationToken);

            if (permissionSuccess)
            {
                logger.LogInformation(
                    "Channel permissions updated for {ChannelId}: ReadOnly={ReadOnly} (PreviousStatus={PreviousStatus}, NewStatus={NewStatus})",
                    request.ChannelId,
                    shouldBeReadOnly,
                    previousStatus,
                    request.Status);
            }
            else
            {
                logger.LogWarning(
                    "Failed to update channel permissions for {ChannelId}: {ErrorMessage}",
                    request.ChannelId,
                    permissionErrorMessage);
            }
        }

        // Move channel to appropriate category based on status
        try
        {
            logger.LogInformation(
                "Attempting to move channel {ChannelId} to category based on status {Status}",
                request.ChannelId,
                request.Status);
            
            var (moveSuccess, moveErrorMessage) = await botGrpcClient.MoveTicketChannelToCategoryAsync(
                request.ChannelId,
                request.Status.ToString(),
                cancellationToken);

            if (moveSuccess)
            {
                logger.LogInformation(
                    "Channel {ChannelId} moved to category based on status {Status}",
                    request.ChannelId,
                    request.Status);
            }
            else
            {
                logger.LogWarning(
                    "Failed to move channel {ChannelId} to category: {ErrorMessage}",
                    request.ChannelId,
                    moveErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Exception while attempting to move channel {ChannelId} to category: {Message}",
                request.ChannelId,
                ex.Message);
        }

        return Ok(ticket);
    }

    [AllowAnonymous]
    [ApiKeyAuth]
    [HttpPatch("Bot/UpdateMessageDiscordId")]
    public async Task<IActionResult> UpdateMessageDiscordIdFromBot([FromBody] BotUpdateMessageDiscordIdRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var message = await context.Messages.FindAsync(request.MessageId);
        if (message == null)
        {
            return NotFound(new { message = "Message not found." });
        }

        message.DiscordMessageId = request.DiscordMessageId;
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Bot updated message {MessageId} with DiscordMessageId={DiscordMessageId}",
            request.MessageId,
            request.DiscordMessageId);

        return Ok(new { success = true });
    }

    [AllowAnonymous]
    [ApiKeyAuth]
    [HttpPost("Bot/UpdateChannelPermissions")]
    public IActionResult UpdateChannelPermissionsFromBot([FromBody] BotUpdateChannelPermissionsRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        logger.LogInformation(
            "Received channel permission update request from bot: ChannelId={ChannelId}, ReadOnly={ReadOnly}",
            request.ChannelId,
            request.ReadOnly);

        // Future implementation: trigger bot via gRPC to adjust channel permissions.
        return Ok(new { success = true });
    }

    [AllowAnonymous]
    [ApiKeyAuth]
    [HttpPut("Bot/UpdateStatus")]
    public async Task<IActionResult> UpdateTicketStatusFromBot([FromBody] BotUpdateTicketStatusRequest request)
    {
        logger.LogInformation(
            "UpdateTicketStatusFromBot called: ChannelId={ChannelId}, Status={Status}",
            request.ChannelId,
            request.Status);
        
        if (!ModelState.IsValid)
        {
            logger.LogWarning("UpdateTicketStatusFromBot: ModelState is invalid");
            return BadRequest(ModelState);
        }

        var ticket = await context.Tickets
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.ChannelId == request.ChannelId);
        if (ticket == null)
        {
            logger.LogWarning("UpdateTicketStatusFromBot: Ticket not found for ChannelId={ChannelId}", request.ChannelId);
            return NotFound(new { message = "Ticket not found." });
        }

        var previousStatus = ticket.Status;
        ticket.Status = request.Status;
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Bot updated ticket status: ChannelId={ChannelId}, Status={Status} (PreviousStatus={PreviousStatus})",
            request.ChannelId,
            request.Status,
            previousStatus);

        // Send status change notification message
        var appUser = await context.Users
            .FirstOrDefaultAsync(u => u.Id == ticket.CreatorId);
        if (appUser == null)
        {
            // Try to find any user as fallback
            appUser = await context.Users.FirstOrDefaultAsync();
        }

        if (appUser != null)
        {
            var statusChangeMessage = new Message
            {
                Content = $"📋 **Ticket status changed:** {previousStatus} → {request.Status}",
                SenderId = appUser.Id,
                Timestamp = DateTime.UtcNow
            };

            ticket.Messages.Add(statusChangeMessage);
            await context.SaveChangesAsync();

            var cancellationToken = HttpContext.RequestAborted;
            var (success, discordMessageId, errorMessage) =
                await botGrpcClient.SendMessageToChannelAsync(request.ChannelId, statusChangeMessage.Content, statusChangeMessage.Id, cancellationToken);

            if (success)
            {
                logger.LogInformation(
                    "Status change message sent to Discord: ChannelId={ChannelId}, MessageId={MessageId}, DiscordMessageId={DiscordMessageId}",
                    request.ChannelId,
                    statusChangeMessage.Id,
                    discordMessageId);
            }
            else
            {
                logger.LogWarning(
                    "Failed to send status change message to Discord: ChannelId={ChannelId}, MessageId={MessageId}, Error={ErrorMessage}",
                    request.ChannelId,
                    statusChangeMessage.Id,
                    errorMessage);
            }
        }

        // Determine if channel permissions need to be updated
        var wasReadOnly = previousStatus is TicketStatus.Completed or TicketStatus.Archived or TicketStatus.Closed;
        var shouldBeReadOnly = request.Status is TicketStatus.Completed or TicketStatus.Archived or TicketStatus.Closed;

        if (wasReadOnly != shouldBeReadOnly)
        {
            try
            {
                var cancellationToken = HttpContext.RequestAborted;
                var (permissionSuccess, permissionErrorMessage) = await botGrpcClient.UpdateChannelPermissionsAsync(request.ChannelId, shouldBeReadOnly, cancellationToken);

                if (permissionSuccess)
                {
                    logger.LogInformation(
                        "Channel permissions updated for {ChannelId}: ReadOnly={ReadOnly} (PreviousStatus={PreviousStatus}, NewStatus={NewStatus})",
                        request.ChannelId,
                        shouldBeReadOnly,
                        previousStatus,
                        request.Status);
                }
                else
                {
                    logger.LogWarning(
                        "Failed to update channel permissions for {ChannelId}: {ErrorMessage}",
                        request.ChannelId,
                        permissionErrorMessage);
                }
            }
            catch (Exception permEx)
            {
                logger.LogError(permEx, 
                    "Exception while updating channel permissions for {ChannelId}: {Message}",
                    request.ChannelId,
                    permEx.Message);
            }
        }

        logger.LogInformation(
            "Reached point before moving channel. ChannelId={ChannelId}, Status={Status}",
            request.ChannelId,
            request.Status);

        // Move channel to appropriate category based on status
        try
        {
            logger.LogInformation(
                "Attempting to move channel {ChannelId} to category based on status {Status}",
                request.ChannelId,
                request.Status);
            
            var (moveSuccess, moveErrorMessage) = await botGrpcClient.MoveTicketChannelToCategoryAsync(
                request.ChannelId,
                request.Status.ToString(),
                HttpContext.RequestAborted);

            if (moveSuccess)
            {
                logger.LogInformation(
                    "Channel {ChannelId} moved to category based on status {Status}",
                    request.ChannelId,
                    request.Status);
            }
            else
            {
                logger.LogWarning(
                    "Failed to move channel {ChannelId} to category: {ErrorMessage}",
                    request.ChannelId,
                    moveErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Exception while attempting to move channel {ChannelId} to category: {Message}, StackTrace: {StackTrace}",
                request.ChannelId,
                ex.Message,
                ex.StackTrace);
        }

        return Ok(ticket);
    }

    //get all tickets for a selected user
    [Authorize] // Override class-level Admin requirement, allow any authenticated user
    [HttpGet("GetTicketsForUser")]
    public async Task<IActionResult> GetTicketsForUser(
        [FromQuery] string? userId = null,
        [FromQuery] bool verify = false,
        [FromQuery] bool includeMessages = false)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // If userId is not provided, use the logged-in user's ID
        string targetUserId;
        if (string.IsNullOrEmpty(userId))
        {
            var loggedInUser = await helperMethods.GetLoggedInUserAsync();
            targetUserId = loggedInUser.Id;
        }
        else
        {
            // If userId is provided and different from logged-in user, require admin role
            var loggedInUser = await helperMethods.GetLoggedInUserAsync();
            if (userId != loggedInUser.Id && !User.IsInRole(Roles.Admin))
            {
                return StatusCode(403, new { message = "You do not have permission to view tickets for other users." });
            }
            targetUserId = userId;
        }

        var ticketsQuery = context.Tickets
            .Where(t => t.CreatorId == targetUserId);

        if (verify || includeMessages)
        {
            ticketsQuery = ticketsQuery.Include(t => t.Messages);
        }

        var tickets = await ticketsQuery.ToListAsync();

        if (verify)
        {
            var cancellationToken = HttpContext.RequestAborted;
            foreach (var ticket in tickets)
            {
                // Fetch all messages from Discord channel
                var (success, discordMessages, errorMessage) = 
                    await botGrpcClient.GetAllChannelMessagesAsync(ticket.ChannelId, cancellationToken);

                if (!success)
                {
                    logger.LogError(
                        "Error fetching messages from Discord for ticket {ChannelId}: {ErrorMessage}",
                        ticket.ChannelId,
                        errorMessage);
                    continue;
                }

                // Get all Discord message IDs that are already in the database
                var existingDiscordMessageIds = ticket.Messages
                    .Where(m => m.DiscordMessageId.HasValue)
                    .Select(m => m.DiscordMessageId!.Value)
                    .ToHashSet();

                // Find messages in Discord that are not in the database
                var missingDiscordMessages = discordMessages
                    .Where(dm => !existingDiscordMessageIds.Contains(dm.discordMessageId))
                    .ToList();

                if (missingDiscordMessages.Count > 0)
                {
                    logger.LogInformation(
                        "Found {MissingCount} missing messages in Discord for ticket {ChannelId}. Syncing...",
                        missingDiscordMessages.Count,
                        ticket.ChannelId);

                    // Store missing messages
                    int syncedCount = 0;
                    int skippedCount = 0;
                    
                    foreach (var discordMessage in missingDiscordMessages)
                    {
                        // Look up the user by Discord ID
                        var appUser = await context.Users
                            .FirstOrDefaultAsync(u => u.DiscordId == discordMessage.authorDiscordId);

                        if (appUser == null)
                        {
                            logger.LogWarning(
                                "Cannot store message {DiscordMessageId} from Discord user {DiscordUserId} in ticket {ChannelId}: User not found in database",
                                discordMessage.discordMessageId,
                                discordMessage.authorDiscordId,
                                ticket.ChannelId);
                            skippedCount++;
                            continue;
                        }

                        // Convert timestamp from Unix seconds to DateTime
                        var messageTimestamp = DateTimeOffset.FromUnixTimeSeconds(discordMessage.timestamp).UtcDateTime;

                        var message = new Message
                        {
                            Content = discordMessage.content,
                            SenderId = appUser.Id,
                            DiscordMessageId = discordMessage.discordMessageId,
                            Timestamp = messageTimestamp
                        };

                        ticket.Messages.Add(message);
                        syncedCount++;
                    }

                    if (syncedCount > 0)
                    {
                        await context.SaveChangesAsync(cancellationToken);
                        logger.LogInformation(
                            "Synced {SyncedCount} missing messages for ticket {ChannelId} (skipped {SkippedCount} due to missing users)",
                            syncedCount,
                            ticket.ChannelId,
                            skippedCount);
                    }
                    else if (skippedCount > 0)
                    {
                        logger.LogWarning(
                            "Could not sync any messages for ticket {ChannelId}: all {SkippedCount} messages were from users not found in database",
                            ticket.ChannelId,
                            skippedCount);
                    }
                }
                else
                {
                    logger.LogInformation(
                        "All messages from Discord are already in database for ticket {ChannelId}",
                        ticket.ChannelId);
                }

                // Also verify existing messages match Discord (optional verification)
                var messagesToVerify = ticket.Messages
                    .Where(m => m.DiscordMessageId.HasValue)
                    .ToList();

                if (messagesToVerify.Count > 0)
                {
                    var verificationList = messagesToVerify
                        .Select(m => (m.Id, m.DiscordMessageId!.Value, m.Content))
                        .ToList();

                    var (allVerified, missingMessageIds, mismatchedMessageIds, verifyError) =
                        await botGrpcClient.VerifyTicketMessagesAsync(ticket.ChannelId, verificationList, cancellationToken);

                    if (!string.IsNullOrEmpty(verifyError))
                    {
                        logger.LogWarning(
                            "Error verifying existing messages for ticket {ChannelId}: {ErrorMessage}",
                            ticket.ChannelId,
                            verifyError);
                    }
                    else if (!allVerified)
                    {
                        logger.LogWarning(
                            "Message verification found issues for ticket {ChannelId}: Missing={MissingCount}, Mismatched={MismatchedCount}",
                            ticket.ChannelId,
                            missingMessageIds.Count,
                            mismatchedMessageIds.Count);
                    }
                }
            }

            // Reload tickets with updated messages if includeMessages is true
            if (includeMessages)
            {
                tickets = await context.Tickets
                    .Where(t => t.CreatorId == targetUserId)
                    .Include(t => t.Messages)
                    .ToListAsync();
            }
        }

        if (!includeMessages)
        {
            var response = tickets.Select(ticket => new TicketSummaryResponse
            {
                ChannelId = ticket.ChannelId,
                CreatorId = ticket.CreatorId,
                Status = ticket.Status,
                Title = ticket.Title
            }).ToList();

            return Ok(response);
        }

        return Ok(tickets);
    }

    [AllowAnonymous]
    [ApiKeyAuth]
    [HttpGet("Bot/GetOpenTickets")]
    public async Task<IActionResult> GetOpenTicketsForBot()
    {
        var tickets = await (from ticket in context.Tickets
            join user in context.Users on ticket.CreatorId equals user.Id into userGroup
            from user in userGroup.DefaultIfEmpty()
            where ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Archived
            select new BotTicketSummaryResponse(
                ticket.ChannelId,
                user != null ? user.DiscordId : 0,
                ticket.Status,
                ticket.Title)).ToListAsync();

        logger.LogInformation("Bot requested open tickets. Count={Count}", tickets.Count);

        return Ok(tickets);
    }

    [AllowAnonymous]
    [ApiKeyAuth]
    [HttpGet("Bot/GetTicketInfo/{channelId}")]
    public async Task<IActionResult> GetTicketInfoForBot(string channelId)
    {
        var ticket = await (from t in context.Tickets
            join user in context.Users on t.CreatorId equals user.Id into userGroup
            from user in userGroup.DefaultIfEmpty()
            where t.ChannelId == channelId
            select new BotTicketSummaryResponse(
                t.ChannelId,
                user != null ? user.DiscordId : 0,
                t.Status,
                t.Title)).FirstOrDefaultAsync();

        if (ticket == null)
        {
            return NotFound(new { message = "Ticket not found." });
        }

        logger.LogInformation("Bot requested ticket info for channel {ChannelId}", channelId);

        return Ok(ticket);
    }
}