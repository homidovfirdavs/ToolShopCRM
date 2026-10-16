using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Idempotency;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Idempotency;

/// <summary>The Idempotency-Key was already used by this user for a DIFFERENT request of the same operation.</summary>
public class OperationKeyReusedException()
    : Exception("This Idempotency-Key was already used for a different request.");

/// <summary>
/// The key bookkeeping shared by order operations (Complete, Return). It must be called INSIDE the caller's serializable
/// transaction: the key row is written in the same transaction as the money / stock changes, so either both exist or
/// neither does (a rejected or rolled-back request never burns its key).
/// </summary>
public static class IdempotencyGate
{
    public const string CompleteScope = "order.complete";
    public const string ReturnScope = "order.return";

    /// <summary>Keys are forgotten after this many days and may then be reused.</summary>
    public const int RetentionDays = 30;

    /// <summary>
    /// Looks the key up. Found and live + same request -> <c>Replayed = true</c> (the caller returns the stored result and
    /// changes nothing). Found and live + DIFFERENT request -> <see cref="OperationKeyReusedException"/>.
    /// Not found (or expired) -> the key is CLAIMED (row inserted, <c>ResultEntityId = 0</c>) and the caller does the work and
    /// then sets <c>ResultEntityId</c>. Claim first, so a losing duplicate never consumes an order / return / payment number.
    /// A concurrent request with the same key fails on the unique index (or with 40001); the caller retries into the replay.
    /// </summary>
    public static async Task<(IdempotencyRecord Row, bool Replayed)> BeginAsync(
        AppDbContext db, string scope, string userId, string key, string requestHash, string resultEntityType)
    {
        var row = await db.IdempotencyRecords.FirstOrDefaultAsync(r =>
            r.Scope == scope && r.UserId == userId && r.Key == key);

        if (row is not null && row.ExpiresAt > DateTime.UtcNow)
        {
            if (row.RequestHash != requestHash)
            {
                throw new OperationKeyReusedException();
            }

            return (row, true);
        }

        if (row is not null)
        {
            // Expired: forget it so the same key can be used again.
            db.IdempotencyRecords.Remove(row);
            await db.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;
        row = new IdempotencyRecord
        {
            Scope = scope,
            UserId = userId,
            Key = key,
            RequestHash = requestHash,
            ResultEntityType = resultEntityType,
            ResultEntityId = 0,
            CreatedAt = now,
            ExpiresAt = now.AddDays(RetentionDays)
        };
        db.IdempotencyRecords.Add(row);
        await db.SaveChangesAsync();

        return (row, false);
    }

    /// <summary>True for failures a fresh attempt can fix: serialization / deadlock, or "someone just stored this key".</summary>
    public static bool IsRetryable(Exception ex) => SerializationFailure.IsRetryable(ex) || IdempotencyKeyConflict.Is(ex);
}
