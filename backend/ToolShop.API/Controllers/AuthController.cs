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
    private readonly MfaPolicy _mfaPolicy;
    private readonly MfaThrottle _mfaThrottle;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        TokenService tokenService,
        RefreshTokenService refreshTokenService,
        ToolShop.Infrastructure.Audit.AuditService auditService,
        MfaPolicy mfaPolicy,
        MfaThrottle mfaThrottle)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _refreshTokenService = refreshTokenService;
        _auditService = auditService;
        _mfaPolicy = mfaPolicy;
        _mfaThrottle = mfaThrottle;
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

        // T2d: a second factor is needed when the role requires it or the user switched it on. No tokens yet.
        if (user.TwoFactorEnabled || _mfaPolicy.IsRequiredFor(roles))
        {
            return Ok(new MfaChallengeResponse
            {
                MfaSetupRequired = !user.TwoFactorEnabled,
                MfaToken = _tokenService.GenerateMfaSessionToken(user)
            });
        }

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

        // T2d: a role that must use MFA cannot keep a session that never passed it.
        if (_mfaPolicy.IsRequiredFor(roles) && !user.TwoFactorEnabled)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user, roles, mfaVerified: user.TwoFactorEnabled);

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

    // ---------- T2d: second factor (TOTP authenticator app + recovery codes) ----------

    private const string MfaIssuer = "ToolShop CRM";

    private async Task<ApplicationUser?> ResolveMfaUserAsync(string mfaToken)
    {
        var userId = _tokenService.ReadMfaSessionToken(mfaToken);
        if (userId is null) return null;
        var user = await _userManager.FindByIdAsync(userId);
        return user is { IsActive: true } ? user : null;
    }

    private static string DigitsOnly(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private IActionResult InvalidMfaSession() =>
        Unauthorized(new { message = "Invalid or expired MFA session. Sign in again." });

    private async Task<MfaLoginResponse> CompleteMfaLoginAsync(ApplicationUser user, IList<string> roles)
    {
        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user, roles, mfaVerified: true);
        var refreshToken = await _refreshTokenService.IssueAsync(user.Id);
        return new MfaLoginResponse
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
    }

    private async Task<bool> RegisterMfaFailureAsync(ApplicationUser user, string why)
    {
        var blocked = _mfaThrottle.RecordFailure(user.Id);
        await TryAuditAsync(user, "Auth.MfaFailed", $"Wrong second-factor code for user '{user.UserName}' ({why}).");
        if (blocked)
            await TryAuditAsync(user, "Auth.MfaBlocked", $"Second factor blocked for 5 minutes for user '{user.UserName}' after repeated failures.");
        return blocked;
    }

    [EnableRateLimiting("auth")]
    [HttpPost("mfa/setup")]
    public async Task<IActionResult> MfaSetup([FromBody] MfaTokenRequest request)
    {
        var user = await ResolveMfaUserAsync(request.MfaToken);
        if (user is null) return InvalidMfaSession();
        if (user.TwoFactorEnabled)
            return BadRequest(new { message = "MFA is already enabled for this user." });

        return await StartMfaSetupAsync(user);
    }

    private async Task<IActionResult> StartMfaSetupAsync(ApplicationUser user)
    {
        await _userManager.ResetAuthenticatorKeyAsync(user);
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
            return StatusCode(500, new { message = "Could not create the authenticator key." });

        var issuer = Uri.EscapeDataString(MfaIssuer);
        var uri = $"otpauth://totp/{issuer}:{Uri.EscapeDataString(user.UserName ?? user.Id)}?secret={key}&issuer={issuer}&digits=6";
        return Ok(new MfaSetupResponse { SharedKey = key, OtpAuthUri = uri });
    }

    // ---------- T2d.2: self-service for a signed-in user (optional MFA for Cashier / Warehouse, new codes for everyone) ----------

    private async Task<ApplicationUser?> CurrentUserAsync()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return null;
        var user = await _userManager.FindByIdAsync(id);
        return user is { IsActive: true } ? user : null;
    }

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("mfa/self/setup")]
    public async Task<IActionResult> MfaSelfSetup()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();
        if (user.TwoFactorEnabled)
            return BadRequest(new { message = "MFA is already enabled." });
        return await StartMfaSetupAsync(user);
    }

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("mfa/self/enable")]
    public async Task<IActionResult> MfaSelfEnable([FromBody] MfaCodeRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();
        if (user.TwoFactorEnabled)
            return BadRequest(new { message = "MFA is already enabled." });
        if (string.IsNullOrEmpty(await _userManager.GetAuthenticatorKeyAsync(user)))
            return BadRequest(new { message = "Start the setup first." });
        if (_mfaThrottle.IsBlocked(user.Id))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many wrong codes. Try again in a few minutes." });

        var valid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, DigitsOnly(request.Code));
        if (!valid)
        {
            await RegisterMfaFailureAsync(user, "self setup");
            return Unauthorized(new { message = "The code is not correct." });
        }

        _mfaThrottle.Reset(user.Id);
        await _userManager.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10)) ?? [];
        await _refreshTokenService.RevokeAllForUserAsync(user.Id);

        var roles = await _userManager.GetRolesAsync(user);
        var response = await CompleteMfaLoginAsync(user, roles);
        response.RecoveryCodes = [.. recoveryCodes];
        await TryAuditAsync(user, "Auth.MfaEnabled", $"MFA enabled by user '{user.UserName}' (self-service).");
        return Ok(response);
    }

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("mfa/self/disable")]
    public async Task<IActionResult> MfaSelfDisable([FromBody] MfaDisableRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();
        if (!user.TwoFactorEnabled)
            return BadRequest(new { message = "MFA is not enabled." });

        var roles = await _userManager.GetRolesAsync(user);
        if (_mfaPolicy.IsRequiredFor(roles))
            return BadRequest(new { message = "MFA is required for your role and cannot be switched off." });
        if (_mfaThrottle.IsBlocked(user.Id))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many wrong codes. Try again in a few minutes." });

        var passwordOk = await _userManager.CheckPasswordAsync(user, request.Password);
        var codeOk = passwordOk && await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, DigitsOnly(request.Code));
        if (!passwordOk || !codeOk)
        {
            await RegisterMfaFailureAsync(user, "self disable");
            return Unauthorized(new { message = "The password or the code is not correct." });
        }

        _mfaThrottle.Reset(user.Id);
        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey");
        await _userManager.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes");
        await _refreshTokenService.RevokeAllForUserAsync(user.Id);
        await TryAuditAsync(user, "Auth.MfaDisabled", $"MFA disabled by user '{user.UserName}'.");
        return NoContent();
    }

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("mfa/self/recovery-codes")]
    public async Task<IActionResult> MfaSelfNewRecoveryCodes([FromBody] MfaCodeRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();
        if (!user.TwoFactorEnabled)
            return BadRequest(new { message = "MFA is not enabled." });
        if (_mfaThrottle.IsBlocked(user.Id))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many wrong codes. Try again in a few minutes." });

        var valid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, DigitsOnly(request.Code));
        if (!valid)
        {
            await RegisterMfaFailureAsync(user, "new recovery codes");
            return Unauthorized(new { message = "The code is not correct." });
        }

        _mfaThrottle.Reset(user.Id);
        var codes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10)) ?? [];
        await TryAuditAsync(user, "Auth.RecoveryCodesRegenerated", $"New recovery codes created for user '{user.UserName}'.");
        return Ok(new MfaRecoveryCodesResponse { RecoveryCodes = [.. codes] });
    }

    [EnableRateLimiting("auth")]
    [HttpPost("mfa/enable")]
    public async Task<IActionResult> MfaEnable([FromBody] MfaEnableRequest request)
    {
        var user = await ResolveMfaUserAsync(request.MfaToken);
        if (user is null) return InvalidMfaSession();
        if (user.TwoFactorEnabled)
            return BadRequest(new { message = "MFA is already enabled for this user." });
        if (string.IsNullOrEmpty(await _userManager.GetAuthenticatorKeyAsync(user)))
            return BadRequest(new { message = "Start the setup first." });

        if (_mfaThrottle.IsBlocked(user.Id))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many wrong codes. Try again in a few minutes." });

        var valid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, DigitsOnly(request.Code));
        if (!valid)
        {
            await RegisterMfaFailureAsync(user, "setup");
            return Unauthorized(new { message = "The code is not correct." });
        }

        _mfaThrottle.Reset(user.Id);
        await _userManager.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10)) ?? [];
        await _refreshTokenService.RevokeAllForUserAsync(user.Id); // sessions that never passed MFA end here

        var roles = await _userManager.GetRolesAsync(user);
        var response = await CompleteMfaLoginAsync(user, roles);
        response.RecoveryCodes = [.. recoveryCodes];

        await TryAuditAsync(user, "Auth.MfaEnabled", $"MFA enabled for user '{user.UserName}'.");
        await _auditService.LogAsync(user.Id, "Login", description: $"User '{user.UserName}' logged in (MFA).",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(response);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("mfa/verify")]
    public async Task<IActionResult> MfaVerify([FromBody] MfaVerifyRequest request)
    {
        var user = await ResolveMfaUserAsync(request.MfaToken);
        if (user is null || !user.TwoFactorEnabled) return InvalidMfaSession();

        if (_mfaThrottle.IsBlocked(user.Id))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many wrong codes. Try again in a few minutes." });

        var usedRecoveryCode = false;
        bool valid;
        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            valid = await _userManager.VerifyTwoFactorTokenAsync(
                user, _userManager.Options.Tokens.AuthenticatorTokenProvider, DigitsOnly(request.Code));
        }
        else if (!string.IsNullOrWhiteSpace(request.RecoveryCode))
        {
            var redeemed = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, request.RecoveryCode.Trim());
            valid = redeemed.Succeeded;
            usedRecoveryCode = valid;
        }
        else
        {
            return BadRequest(new { message = "Enter the code from the authenticator app or a recovery code." });
        }

        if (!valid)
        {
            await RegisterMfaFailureAsync(user, usedRecoveryCode ? "recovery code" : "code");
            return Unauthorized(new { message = "The code is not correct." });
        }

        _mfaThrottle.Reset(user.Id);
        var roles = await _userManager.GetRolesAsync(user);
        var response = await CompleteMfaLoginAsync(user, roles);

        if (usedRecoveryCode)
        {
            var left = await _userManager.CountRecoveryCodesAsync(user);
            await TryAuditAsync(user, "Auth.RecoveryCodeUsed", $"Recovery code used by user '{user.UserName}'; {left} left.");
        }
        await _auditService.LogAsync(user.Id, "Login", description: $"User '{user.UserName}' logged in (MFA).",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(response);
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