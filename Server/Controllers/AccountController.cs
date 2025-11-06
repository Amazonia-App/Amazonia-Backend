using Core.Interfaces;
using Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.DTOModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.DBContext;

namespace Server.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class AccountController(IAccountHandler accountHandler, ILogger<AccountController> logger, UserManager<AppUser> userManager, AppDbContext db) : ControllerBase
{

    [HttpPut("changeMinecraftUsername")]
    public async Task<ActionResult> ChangeMinecraftUsername([FromBody] ChangeMinecraftUsernameRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await accountHandler.ChangeMinecraftUsernameAsync(request.MinecraftUsername);
            logger.LogInformation("Minecraft username updated");
            return Ok(new { message = "Minecraft username updated successfully" });
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Minecraft username update failed: {Error}", e.Message);
            return BadRequest(new {message = e.Message});
        }
    }
    
    [HttpGet("getUser")]
    public async Task<ActionResult<AppUserDto>> GetUser([FromQuery] string userId)
    {
        try
        {
            var user = await accountHandler.GetUserAsync(userId);
            var roles = await userManager.GetRolesAsync(user);
            return Ok(new AppUserDto(user, roles));
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Get user failed: {Error}", e.Message);
            return BadRequest(new {message = e.Message});
        }
    }
    
    //[Authorize(Roles = Roles.Admin)]
    [HttpGet("findUser")]
    public async Task<ActionResult<PagedResult<AppUserDto>>> FindUser([FromQuery] string query, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        try
        {
            var (users, totalCount) = await accountHandler.FindUserAsync(query, page, pageSize);

            var userIds = users.Select(u => u.Id).ToList();
            var rolesLookup = await (from ur in db.UserRoles
                                     join r in db.Roles on ur.RoleId equals r.Id
                                     where userIds.Contains(ur.UserId)
                                     select new { ur.UserId, r.Name })
                                    .ToListAsync();

            var groupedRoles = rolesLookup
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

            var dtoUsers = users.Select(u =>
            {
                groupedRoles.TryGetValue(u.Id, out var r);
                return new AppUserDto(u, r ?? new List<string>());
            }).ToList();
            
            // Create paged result with proper total count
            var result = new PagedResult<AppUserDto>
            {
                Items = dtoUsers,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            };
            
            return Ok(result);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Find user failed: {Error}", e.Message);
            return BadRequest(new {message = e.Message});
        }
    }
}