using Npgsql;

namespace ToolShop.Infrastructure.Persistence;

/// <summary>
/// Recognises the PostgreSQL "serialization_failure" (40001) and "deadlock_detected" (40P01) errors that a
/// SERIALIZABLE transaction raises when it loses a race. Depending on where it happens it arrives as a bare
/// PostgresException (queries, Commit) or wrapped by EF Core: DbUpdateException (SaveChanges) and, with the Npgsql
/// execution strategy, InvalidOperationException("...transient failure") around that. So the whole exception chain
/// is inspected.
/// </summary>
public static class SerializationFailure
{
    public static bool IsRetryable(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: "40001" or "40P01" })
            {
                return true;
            }
        }

        return false;
    }
}
