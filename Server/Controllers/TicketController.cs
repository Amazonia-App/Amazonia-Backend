using Core.Interfaces;
using Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.DBContext;
using Server.DTOModels;

namespace Server.Controllers;

[Authorize(Roles = Roles.Admin)]
[Route("api/[controller]")]
public class TicketController(AppDbContext context, IHelperMethods helperMethods, ILogger<TicketController> logger) : ControllerBase
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
        
        // TODO: send the message to the channel via the bot

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

        ticket.Status = request.Status;
        await context.SaveChangesAsync();

        logger.LogInformation("Ticket status updated: ChannelId={ChannelId}, Status={Status}", request.ChannelId, request.Status);

        return Ok(ticket);
    }
}