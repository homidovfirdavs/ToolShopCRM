using System.ComponentModel.DataAnnotations;

namespace ToolShop.API.DTOs.Auth;

public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}