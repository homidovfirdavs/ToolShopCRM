using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Auth;

public class RefreshTokenService
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokenService;
    private const int RefreshTokenDays = 7;

    public RefreshTokenService(AppDbContext db, TokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    public async Task<string> IssueAsync(string userId)
    {
        var rawToken = _tokenService.GenerateRefreshToken();
        var tokenHash = _tokenService.HashToken(rawToken);

        var refreshToken = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays)
        };

        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync();

        return rawToken;
    }

    public async Task<(bool Success, string? UserId, string? NewRawToken)> RotateAsync(string rawToken)
    {
        var tokenHash = _tokenService.HashToken(rawToken);

        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (existing is null || !existing.IsActive)
        {
            return (false, null, null);
        }

        existing.RevokedAt = DateTime.UtcNow;

        var newRawToken = _tokenService.GenerateRefreshToken();
        var newHash = _tokenService.HashToken(newRawToken);

        var newToken = new RefreshToken
        {
            UserId = existing.UserId,
            TokenHash = newHash,
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays)
        };

        _db.RefreshTokens.Add(newToken);
        await _db.SaveChangesAsync();

        existing.ReplacedByTokenId = newToken.Id;
        await _db.SaveChangesAsync();

        return (true, existing.UserId, newRawToken);
    }

    public async Task RevokeAsync(string rawToken)
    {
        var tokenHash = _tokenService.HashToken(rawToken);

        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (existing is not null && existing.RevokedAt is null)
        {
            existing.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}