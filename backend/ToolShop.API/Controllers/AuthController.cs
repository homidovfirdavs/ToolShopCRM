using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ToolShop.API.DTOs.Auth;
using ToolShop.Domain.Entities;
using ToolShop.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
        private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly TokenService _tokenService;
    private readonly RefreshTokenService _refreshTokenService;
    private readonly ToolShop.Infrastructure.Audit.AuditService _auditService;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        TokenService tokenService,
        RefreshTokenService refreshTokenService,
        ToolShop.Infrastructure.Audit.AuditService auditService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _refreshTokenService = refreshTokenService;
        _auditService = auditService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var user = await _userManager.FindByNameAsync(request.UsernameOrEmail)
            ?? await _userManager.FindByEmailAsync(request.UsernameOrEmail);

        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { message = "Invalid username/email or password." });
        }

                var signInResult = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            return Unauthorized(new { message = "Account temporarily locked due to multiple failed attempts. Try again later." });
        }

        if (!signInResult.Succeeded)
        {
            return Unauthorized(new { message = "Invalid username/email or password." });
        }

        var roles = await _userManager.GetRolesAsync(user);
        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user, roles);
        var refreshToken = await _refreshTokenService.IssueAsync(user.Id);

        var response = new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            User = new UserInfo
            {
                Id = user.Id,
                Username = user.UserName ?? string.Empty,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                Roles = [.. roles]
            }
        };

    await _auditService.LogAsync(
            user.Id,
            "Login",
            description: $"User '{user.UserName}' logged in.",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());


        return Ok(response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { message = "Refresh token is required." });
        }

        var (success, userId, newRawToken) = await _refreshTokenService.RotateAsync(request.RefreshToken);

        if (!success || userId is null || newRawToken is null)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        var roles = await _userManager.GetRolesAsync(user);
        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user, roles);

        return Ok(new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRawToken,
            ExpiresAt = expiresAt,
            User = new UserInfo
            {
                Id = user.Id,
                Username = user.UserName ?? string.Empty,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                Roles = [.. roles]
            }
        });
    }
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            return Unauthorized();
        }

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new UserInfo
        {
            Id = user.Id,
            Username = user.UserName ?? string.Empty,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Roles = [.. roles]
        });
    
    }

       [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await _refreshTokenService.RevokeAsync(request.RefreshToken);
        }

        return NoContent();
    }


}