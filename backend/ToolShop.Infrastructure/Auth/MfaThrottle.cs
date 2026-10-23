using System.Collections.Concurrent;

namespace ToolShop.Infrastructure.Auth;

/// <summary>
/// T2d: limits wrong second-factor codes per user. A correct password does NOT reset this counter, so a stolen password
/// cannot be combined with endless code guessing. State is kept in memory (single API instance); a restart clears it.
/// </summary>
public class MfaThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan BlockFor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (int Failures, DateTime FirstUtc, DateTime? BlockedUntilUtc)> _state = new();

    public bool IsBlocked(string userId)
    {
        if (!_state.TryGetValue(userId, out var s)) return false;
        var now = DateTime.UtcNow;
        if (s.BlockedUntilUtc is { } until && until > now) return true;
        if (s.BlockedUntilUtc is not null || now - s.FirstUtc > Window) _state.TryRemove(userId, out _);
        return false;
    }

    /// <returns>true when this failure blocked the user.</returns>
    public bool RecordFailure(string userId)
    {
        var now = DateTime.UtcNow;
        var s = _state.AddOrUpdate(
            userId,
            _ => (1, now, null),
            (_, old) =>
            {
                if (old.BlockedUntilUtc is { } until && until > now) return old;
                if (old.BlockedUntilUtc is not null || now - old.FirstUtc > Window) return (1, now, null);
                var failures = old.Failures + 1;
                return (failures, old.FirstUtc, failures >= MaxFailures ? now + BlockFor : (DateTime?)null);
            });
        return s.BlockedUntilUtc is { } blocked && blocked > now;
    }

    public void Reset(string userId) => _state.TryRemove(userId, out _);
}
