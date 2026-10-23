using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Users;
using ToolShop.Domain.Authorization;
using ToolShop.Domain.Entities;
using ToolShop.Infrastructure.Audit;
using ToolShop.Infrastructure.Identity;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Controllers;

/// <summary>
/// User administration (Admin only, via the Users.Manage permission).
/// Users are never deleted - they are deactivated. Passwords are never logged or returned.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = Permissions.UsersManage)]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public UsersController(UserManager<ApplicationUser> users, AppDbContext db, AuditService audit)
    {
        _users = users;
        _db = db;
        _audit = audit;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IActionResult Fail(int status, string code, string message) =>
        StatusCode(status, new { code, message });

    private IActionResult IdentityFail(IdentityResult result) =>
        Fail(400, "VALIDATION_FAILED", string.Join(" ", result.Errors.Select(e => e.Description)));

    private async Task<UserListItemDto> ToDtoAsync(ApplicationUser u)
    {
        var roles = await _users.GetRolesAsync(u);
        return new UserListItemDto
        {
            Id = u.Id,
            Username = u.UserName ?? string.Empty,
            FullName = u.FullName,
            Email = u.Email ?? string.Empty,
            Role = roles.FirstOrDefault() ?? string.Empty,
            IsActive = u.IsActive,
            IsLockedOut = u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow,
            MfaEnabled = u.TwoFactorEnabled,
            CreatedAt = u.CreatedAt
        };
    }

    /// <summary>Number of ACTIVE users in the Admin role.</summary>
    private async Task<int> ActiveAdminCountAsync() =>
        (await _users.GetUsersInRoleAsync("Admin")).Count(u => u.IsActive);

    private async Task RevokeRefreshTokensAsync(string userId)
    {
        var now = DateTime.UtcNow;
        await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, (DateTime?)now));
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var all = await _users.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();
        var result = new List<UserListItemDto>(all.Count);
        foreach (var u in all) result.Add(await ToDtoAsync(u));
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (!RoleSeeder.Roles.Contains(request.Role))
            return Fail(400, "INVALID_ROLE", "Unknown role.");

        var username = request.Username.Trim();
        var email = request.Email.Trim();

        if (await _users.FindByNameAsync(username) is not null)
            return Fail(409, "USERNAME_TAKEN", "A user with this username already exists.");
        if (await _users.FindByEmailAsync(email) is not null)
            return Fail(409, "EMAIL_TAKEN", "A user with this email already exists.");

        var user = new ApplicationUser
        {
            UserName = username,
            Email = email,
            FullName = request.FullName.Trim(),
            IsActive = true,
            EmailConfirmed = true
        };

        var created = await _users.CreateAsync(user, request.Password);
        if (!created.Succeeded) return IdentityFail(created);

        var roleResult = await _users.AddToRoleAsync(user, request.Role);
        if (!roleResult.Succeeded)
        {
            await _users.DeleteAsync(user); // brand-new user without any history; roll back
            return IdentityFail(roleResult);
        }

        await _audit.LogAsync(CurrentUserId, "UserCreated", "User", user.Id,
            $"User '{username}' created with role {request.Role}.", Ip);

        return CreatedAtAction(nameof(List), new { }, await ToDtoAsync(user));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _users.FindByIdAsync(id);
        if (user is null) return Fail(404, "USER_NOT_FOUND", "User not found.");

        if (!RoleSeeder.Roles.Contains(request.Role))
            return Fail(400, "INVALID_ROLE", "Unknown role.");

        var email = request.Email.Trim();
        var other = await _users.FindByEmailAsync(email);
        if (other is not null && other.Id != user.Id)
            return Fail(409, "EMAIL_TAKEN", "A user with this email already exists.");

        var currentRoles = await _users.GetRolesAsync(user);
        var oldRole = currentRoles.FirstOrDefault() ?? string.Empty;
        var roleChanges = !currentRoles.SequenceEqual(new[] { request.Role });

        if (roleChanges)
        {
            if (user.Id == CurrentUserId)
                return Fail(409, "CANNOT_CHANGE_OWN_ROLE", "You cannot change your own role.");

            if (currentRoles.Contains("Admin") && user.IsActive && await ActiveAdminCountAsync() <= 1)
                return Fail(409, "LAST_ADMIN", "The last active administrator cannot lose the Admin role.");
        }

        user.FullName = request.FullName.Trim();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var emailResult = await _users.SetEmailAsync(user, email);
            if (!emailResult.Succeeded) return IdentityFail(emailResult);
        }
        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded) return IdentityFail(updated);

        if (roleChanges)
        {
            var remove = await _users.RemoveFromRolesAsync(user, currentRoles);
            if (!remove.Succeeded) return IdentityFail(remove);
            var add = await _users.AddToRoleAsync(user, request.Role);
            if (!add.Succeeded) return IdentityFail(add);

            // Force a fresh login so the new permissions apply (refresh tokens are re-issued with the new role).
            await RevokeRefreshTokensAsync(user.Id);

            await _audit.LogAsync(CurrentUserId, "UserRoleChanged", "User", user.Id,
                $"User '{user.UserName}' role changed {oldRole} -> {request.Role}.", Ip);
        }

        await _audit.LogAsync(CurrentUserId, "UserUpdated", "User", user.Id,
            $"User '{user.UserName}' updated.", Ip);

        return Ok(await ToDtoAsync(user));
    }

    [HttpPost("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return Fail(404, "USER_NOT_FOUND", "User not found.");

        if (user.Id == CurrentUserId)
            return Fail(409, "CANNOT_DEACTIVATE_SELF", "You cannot deactivate your own account.");

        if (user.IsActive && await _users.IsInRoleAsync(user, "Admin") && await ActiveAdminCountAsync() <= 1)
            return Fail(409, "LAST_ADMIN", "The last active administrator cannot be deactivated.");

        if (user.IsActive)
        {
            user.IsActive = false;
            var result = await _users.UpdateAsync(user);
            if (!result.Succeeded) return IdentityFail(result);

            await RevokeRefreshTokensAsync(user.Id);
            await _audit.LogAsync(CurrentUserId, "UserDeactivated", "User", user.Id,
                $"User '{user.UserName}' deactivated.", Ip);
        }

        return Ok(await ToDtoAsync(user));
    }

    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return Fail(404, "USER_NOT_FOUND", "User not found.");

        if (!user.IsActive)
        {
            user.IsActive = true;
            var result = await _users.UpdateAsync(user);
            if (!result.Succeeded) return IdentityFail(result);

            await _audit.LogAsync(CurrentUserId, "UserActivated", "User", user.Id,
                $"User '{user.UserName}' activated.", Ip);
        }

        return Ok(await ToDtoAsync(user));
    }

    /// <summary>
    /// A user lost the phone and the recovery codes: switch their MFA off so they set it up again at the next login.
    /// An Admin cannot reset their own MFA (a stolen admin session must not be able to do that).
    /// </summary>
    [HttpPost("{id}/mfa/reset")]
    public async Task<IActionResult> ResetMfa(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return Fail(404, "USER_NOT_FOUND", "User not found.");
        if (user.Id == CurrentUserId)
            return Fail(400, "CANNOT_RESET_OWN_MFA", "You cannot reset your own MFA. Use a recovery code or ask the owner to run Reset-AdminMfa.ps1.");

        await _users.SetTwoFactorEnabledAsync(user, false);
        await _users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey");
        await _users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes");
        await RevokeRefreshTokensAsync(user.Id); // every existing session of that user ends

        await _audit.LogAsync(CurrentUserId, "UserMfaReset", "User", user.Id,
            $"MFA of user '{user.UserName}' was reset.", Ip);

        return NoContent();
    }

    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetPasswordRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _users.FindByIdAsync(id);
        if (user is null) return Fail(404, "USER_NOT_FOUND", "User not found.");

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded) return IdentityFail(result);

        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);
        await RevokeRefreshTokensAsync(user.Id); // every existing session of that user ends

        // The password itself is never written to the audit log.
        await _audit.LogAsync(CurrentUserId, "UserPasswordReset", "User", user.Id,
            $"Password of user '{user.UserName}' was reset.", Ip);

        return NoContent();
    }
}
