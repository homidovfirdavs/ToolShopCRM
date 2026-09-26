using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
}