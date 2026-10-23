using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ToolShop.Domain.Entities;
using ToolShop.Domain.Authorization;

namespace ToolShop.Infrastructure.Auth;

public class TokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string AccessToken, DateTime ExpiresAt) GenerateAccessToken(
        ApplicationUser user, IList<string> roles, bool mfaVerified = false)
    {
        var jwtKey = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var issuer = _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        var audience = _configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
        var minutes = _configuration.GetValue<int>("Jwt:AccessTokenMinutes", 15);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
        };

        // T2d: set only when the user passed the second factor in this session chain.
        if (mfaVerified) claims.Add(new Claim(MfaClaimType, "true"));

       foreach (var role in roles)
{
    claims.Add(new Claim(ClaimTypes.Role, role));

    if (Permissions.RolePermissions.TryGetValue(role, out var permissions))
    {
        foreach (var permission in permissions.Distinct())
        {
            claims.Add(new Claim("permission", permission));
        }
    }
}
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(minutes);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return (accessToken, expiresAt);
    }

    public const string MfaClaimType = "mfa";
    private const string MfaPurpose = "mfa-session";
    private static readonly TimeSpan MfaSessionLifetime = TimeSpan.FromMinutes(5);

    private string MfaAudience() =>
        (_configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.")) + ":mfa";

    /// <summary>
    /// A short-lived token that proves "the password was correct" and nothing else. It has its own audience,
    /// so the API never accepts it as an access token.
    /// </summary>
    public string GenerateMfaSessionToken(ApplicationUser user)
    {
        var jwtKey = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var issuer = _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: MfaAudience(),
            claims: [new Claim(JwtRegisteredClaimNames.Sub, user.Id), new Claim("purpose", MfaPurpose)],
            expires: DateTime.UtcNow.Add(MfaSessionLifetime),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Returns the user id of a valid MFA session token, otherwise null.</summary>
    public string? ReadMfaSessionToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var jwtKey = _configuration["Jwt:Key"];
        var issuer = _configuration["Jwt:Issuer"];
        if (jwtKey is null || issuer is null) return null;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = MfaAudience(),
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out var validated);

            var jwt = (JwtSecurityToken)validated;
            if (jwt.Claims.FirstOrDefault(c => c.Type == "purpose")?.Value != MfaPurpose) return null;
            return jwt.Subject;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }
}