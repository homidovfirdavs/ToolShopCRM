using Npgsql;

namespace ToolShop.Infrastructure.Persistence;

/// <summary>
/// Recognises "someone else just stored this Idempotency-Key" (unique violation on the key index), as opposed to any
/// other unique violation. Arrives bare or wrapped (DbUpdateException / retrying execution strategy), so the whole
/// exception chain is inspected. The caller retries: the next attempt finds the stored record and replays it.
/// </summary>
public static class IdempotencyKeyConflict
{
    public const string IndexName = "IX_IdempotencyRecords_Scope_UserId_Key";

    public static bool Is(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: "23505" } pg && pg.ConstraintName == IndexName)
            {
                return true;
            }
        }

        return false;
    }
}
