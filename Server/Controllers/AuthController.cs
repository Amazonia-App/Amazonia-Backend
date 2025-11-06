using System.Security.Claims;
using Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.DTOModels;

namespace Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController(
        UserManager<AppUser> userManager, 
        SignInManager<AppUser> signInManager,
        IConfiguration configuration,
        ILogger<AuthController> logger)
        : ControllerBase
    {
        [HttpGet("login")]
        public IActionResult Login([FromQuery] string returnUrl = null)
        {
            var redirectUrl = Url.Action("DiscordCallback", "Auth");
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };

            return Challenge(properties, "Discord"); // "Discord" must match your OAuth scheme name
        }

        [HttpGet("callback")]
        public async Task<IActionResult> DiscordCallback([FromQuery] string? returnUrl = null)
        {
            var result = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);

            if (!result.Succeeded)
            {
                return Redirect("/login?error=external_login_failed");
            }

            // Get frontend URL from configuration
            var frontendUrl = configuration["FrontendUrl"];
            if (string.IsNullOrWhiteSpace(frontendUrl))
            {
                logger.LogError("FrontendUrl is not configured in appsettings");
                return StatusCode(500, new { message = "Frontend URL not configured" });
            }

            // User creation and sign-in is handled in OnCreatingTicket
            // Redirect to the configured frontend URL
            return Redirect(frontendUrl);
        }
        
        [HttpGet("Check")]
        public async Task<IActionResult> Check()
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
            {
                return Ok(new { isAuthenticated = false });
            }
            var roles = await userManager.GetRolesAsync(user);
            return Ok(new { isAuthenticated = true, user = new AppUserDto(user, roles) });
        }


        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Ok(new { Message = "Logged out" });
        }
        
        
        [Authorize]
        [HttpGet("getRoles")]
        public async Task<ActionResult<List<string>>> GetRoles()
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("User not found");
            }
            var roles = await userManager.GetRolesAsync(user);
            return Ok(roles);
        }

        [Authorize(Roles = Roles.Admin)]
        [HttpGet("getAllRoles")]
        public async Task<ActionResult<List<string>>> GetAllRoles()
        {
            return Ok(Roles.GetAllRoles);
        }

        
        [Route("AddToRole")]
        [HttpPost]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult> AddToRoleAsync([FromBody] AddToRoleRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return BadRequest(new { message = "User ID is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Role) || !Roles.IsValidRole(request.Role))
            {
                return BadRequest(new { message = "Invalid role" });
            }

            var user = await userManager.FindByIdAsync(request.UserId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }
        
            var result = await userManager.AddToRoleAsync(user, request.Role);
        
            if (result.Succeeded)
            {
                logger.LogInformation("Role added to user: UserId={UserId}, Role={Role}", request.UserId, request.Role);
                return Ok(new { message = "Role added successfully" });
            }
        
            return BadRequest(new { message = string.Join(", ", result.Errors.Select(e => e.Description)) });
        }

        [Route("RemoveFromRole")]
        [HttpPost]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult> RemoveFromRoleAsync([FromBody] RemoveFromRoleRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return BadRequest(new { message = "User ID is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Role) || !Roles.IsValidRole(request.Role))
            {
                return BadRequest(new { message = "Invalid role" });
            }

            var user = await userManager.FindByIdAsync(request.UserId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }
        
            var result = await userManager.RemoveFromRoleAsync(user, request.Role);
        
            if (result.Succeeded)
            {
                logger.LogInformation("Role removed from user: UserId={UserId}, Role={Role}", request.UserId, request.Role);
                return Ok(new { message = "Role removed successfully" });
            }
        
            return BadRequest(new { message = string.Join(", ", result.Errors.Select(e => e.Description)) });
        }
    }
}