using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using ToolShop.Domain.Entities;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/security")]
[Authorize]
public class SecurityController : ControllerBase
{
    [HttpGet("admin-test")]
    [Authorize(Roles = "Admin")]
    public IActionResult AdminTest()
    {
        return Ok(new { message = "You are an Admin.", user = User.Identity?.Name });
    }

    [HttpGet("cashier-test")]
    [Authorize(Roles = "Admin,Cashier")]
    public IActionResult CashierTest()
    {
        return Ok(new { message = "You are an Admin or Cashier.", user = User.Identity?.Name });
    }

    [HttpGet("warehouse-test")]
    [Authorize(Roles = "Admin,Warehouse")]
    public IActionResult WarehouseTest()
    {
        return Ok(new { message = "You are an Admin or Warehouse.", user = User.Identity?.Name });
    }
    
    [AllowAnonymous]
    [HttpPost("dev/create-test-user")]
    public async Task<IActionResult> CreateTestUser(
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] IWebHostEnvironment env,
        [FromQuery] string username,
        [FromQuery] string password,
        [FromQuery] string role)
    {
        if (!env.IsDevelopment())
        {
            return NotFound();
        }

        var existing = await userManager.FindByNameAsync(username);
        if (existing is not null)
        {
            return Ok(new { message = $"User '{username}' already exists." });
        }

        var user = new ApplicationUser
        {
            UserName = username,
            Email = $"{username}@toolshop.local",
            FullName = username,
            IsActive = true,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        await userManager.AddToRoleAsync(user, role);

        return Ok(new { message = $"Test user '{username}' created with role '{role}'." });
    }

}