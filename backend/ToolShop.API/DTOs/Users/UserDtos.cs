using System.ComponentModel.DataAnnotations;

namespace ToolShop.API.DTOs.Users;

public class UserListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsLockedOut { get; set; }
    public bool MfaEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateUserRequest
{
    [Required, StringLength(64, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;
}

public class UpdateUserRequest
{
    [Required, StringLength(128)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [Required, StringLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}
