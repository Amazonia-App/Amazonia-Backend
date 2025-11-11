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

    [AllowAnonymous]
    [ApiKeyAuth]
    [HttpPost("Bot/CreateTicket")]
    public async Task<IActionResult> CreateTicketFromBot([FromBody] BotCreateTicketRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var existingTicket = await context.Tickets.FindAsync(request.ChannelId);
        if (existingTicket != null)
        {
            return Conflict(new { message = "Ticket already exists for this channel." });
        }

        var appUser = await context.Users.FirstOrDefaultAsync(u => u.DiscordId == request.DiscordUserId);
        if (appUser == null)
        {
            return NotFound(new { message = "User not found. User must authenticate with Discord first." });
        }

        var ticket = new Ticket
        {
            ChannelId = request.ChannelId,
            Status = TicketStatus.Created,
            CreatorId = appUser.Id
        };

        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Bot created ticket: ChannelId={ChannelId}, CreatorDiscordId={CreatorDiscordId}",
            request.ChannelId,
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

        var ticket = await context.Tickets.FindAsync(request.ChannelId);
        if (ticket == null)
        {
            return NotFound("Ticket not found.");
        }

        var previousStatus = ticket.Status;
        ticket.Status = request.Status;
        await context.SaveChangesAsync();

        logger.LogInformation("Ticket status updated: ChannelId={ChannelId}, Status={Status}", request.ChannelId, request.Status);

        var wasReadOnly = previousStatus is TicketStatus.Completed or TicketStatus.Archived or TicketStatus.Closed;
        var shouldBeReadOnly = request.Status is TicketStatus.Completed or TicketStatus.Archived or TicketStatus.Closed;

        if (wasReadOnly != shouldBeReadOnly)
        {
            var cancellationToken = HttpContext.RequestAborted;
            var (success, errorMessage) = await botGrpcClient.UpdateChannelPermissionsAsync(request.ChannelId, shouldBeReadOnly, cancellationToken);

            if (success)
            {
                logger.LogInformation(
                    "Channel permissions updated for {ChannelId}: ReadOnly={ReadOnly}",
                    request.ChannelId,
                    shouldBeReadOnly);
            }
            else
            {
                logger.LogWarning(
                    "Failed to update channel permissions for {ChannelId}: {ErrorMessage}",
                    request.ChannelId,
                    errorMessage);
            }
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

    //get all tickets for a selected user
    [HttpGet("GetTicketsForUser")]
    public async Task<IActionResult> GetTicketsForUser(
        [FromQuery] string userId,
        [FromQuery] bool verify = false,
        [FromQuery] bool includeMessages = false)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var ticketsQuery = context.Tickets
            .Where(t => t.CreatorId == userId);

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
                var messagesToVerify = ticket.Messages
                    .Where(m => m.DiscordMessageId.HasValue)
                    .ToList();

                if (messagesToVerify.Count == 0)
                {
                    continue;
                }

                var verificationList = messagesToVerify
                    .Select(m => (m.Id, m.DiscordMessageId!.Value, m.Content))
                    .ToList();

                var (allVerified, missingMessageIds, mismatchedMessageIds, errorMessage) =
                    await botGrpcClient.VerifyTicketMessagesAsync(ticket.ChannelId, verificationList, cancellationToken);

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    logger.LogError(
                        "Error verifying messages for ticket {ChannelId}: {ErrorMessage}",
                        ticket.ChannelId,
                        errorMessage);
                    continue;
                }

                if (allVerified)
                {
                    logger.LogInformation("All messages verified for ticket {ChannelId}", ticket.ChannelId);
                }
                else
                {
                    logger.LogWarning(
                        "Message verification failed for ticket {ChannelId}: Missing={MissingCount}, Mismatched={MismatchedCount}",
                        ticket.ChannelId,
                        missingMessageIds.Count,
                        mismatchedMessageIds.Count);
                }
            }
        }

        if (!includeMessages)
        {
            var response = tickets.Select(ticket => new TicketSummaryResponse
            {
                ChannelId = ticket.ChannelId,
                CreatorId = ticket.CreatorId,
                Status = ticket.Status
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
                ticket.Status)).ToListAsync();

        logger.LogInformation("Bot requested open tickets. Count={Count}", tickets.Count);

        return Ok(tickets);
    }
}