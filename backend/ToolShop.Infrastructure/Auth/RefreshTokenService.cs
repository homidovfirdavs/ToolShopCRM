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

    // A token that was rotated moments ago may legitimately be presented again (two tabs / parallel 401s).
    private static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(30);

    public async Task<(bool Success, string? UserId, string? NewRawToken)> RotateAsync(string rawToken)
    {
        var tokenHash = _tokenService.HashToken(rawToken);
        var now = DateTime.UtcNow;

        var existing = await _db.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (existing is null)
        {
            return (false, null, null);
        }

        if (existing.RevokedAt is not null)
        {
            // Re-use of an already revoked/rotated token. Outside the grace period this indicates theft:
            // revoke every active refresh token of this user.
            if (now - existing.RevokedAt.Value > ReuseGracePeriod)
            {
                await _db.RefreshTokens
                    .Where(rt => rt.UserId == existing.UserId && rt.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, (DateTime?)now));
            }

            return (false, null, null);
        }

        if (existing.ExpiresAt <= now)
        {
            return (false, null, null);
        }

        // Atomically claim the token: only one concurrent request can flip RevokedAt from NULL.
        var claimed = await _db.RefreshTokens
            .Where(rt => rt.Id == existing.Id && rt.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, (DateTime?)now));

        if (claimed == 0)
        {
            return (false, null, null);
        }

        var newRawToken = _tokenService.GenerateRefreshToken();
        var newToken = new RefreshToken
        {
            UserId = existing.UserId,
            TokenHash = _tokenService.HashToken(newRawToken),
            ExpiresAt = now.AddDays(RefreshTokenDays)
        };

        _db.RefreshTokens.Add(newToken);
        await _db.SaveChangesAsync();

        var newId = newToken.Id;
        await _db.RefreshTokens
            .Where(rt => rt.Id == existing.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.ReplacedByTokenId, rt => newId));

        return (true, existing.UserId, newRawToken);
    }

    /// <summary>Ends every active session of one user (used when MFA is switched on or reset).</summary>
    public async Task RevokeAllForUserAsync(string userId)
    {
        var now = DateTime.UtcNow;
        await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, (DateTime?)now));
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