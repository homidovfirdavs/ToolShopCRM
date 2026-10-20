using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ToolShop.API.DTOs.Auth;
using ToolShop.Domain.Entities;
using ToolShop.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;

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

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var user = await _userManager.FindByNameAsync(request.UsernameOrEmail)
            ?? await _userManager.FindByEmailAsync(request.UsernameOrEmail);

        if (user is null)
        {
            // The typed name is NOT stored: people sometimes type their password into the username field.
            await TryAuditFailureAsync(null, "Login failed: unknown user.");
            return Unauthorized(new { message = "Invalid username/email or password." });
        }

        if (!user.IsActive)
        {
            await TryAuditFailureAsync(user, $"Login failed: user '{user.UserName}' is deactivated.");
            return Unauthorized(new { message = "Invalid username/email or password." });
        }

        var wasLockedOut = await _userManager.IsLockedOutAsync(user);
        var signInResult = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            if (wasLockedOut)
                await TryAuditFailureAsync(user, $"Login refused: user '{user.UserName}' is locked out.");
            else
                await TryAuditAsync(user, "Auth.LockedOut", $"User '{user.UserName}' was locked out after repeated failed logins.");

            return Unauthorized(new { message = "Account temporarily locked due to multiple failed attempts. Try again later." });
        }

        if (!signInResult.Succeeded)
        {
            await TryAuditFailureAsync(user, $"Login failed: wrong password for user '{user.UserName}'.");
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

    [EnableRateLimiting("auth")]
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

    private Task TryAuditFailureAsync(ApplicationUser? user, string description) =>
        TryAuditAsync(user, "Auth.LoginFailed", description);

    // A failed audit write must never change the login response.
    private async Task TryAuditAsync(ApplicationUser? user, string action, string description)
    {
        try
        {
            await _auditService.LogAsync(
                user?.Id,
                action,
                user is null ? null : "User",
                user?.Id,
                description,
                HttpContext.Connection.RemoteIpAddress?.ToString());
        }
        catch
        {
            // intentionally swallowed
        }
    }
}