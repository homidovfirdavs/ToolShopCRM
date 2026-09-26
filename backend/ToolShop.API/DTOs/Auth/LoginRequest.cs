using System.ComponentModel.DataAnnotations;

namespace ToolShop.API.DTOs.Auth;

public class LoginRequest
{
    [Required]
    public string UsernameOrEmail { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}