namespace ToolShop.Domain.Entities.Idempotency;

/// <summary>
/// "This user already did this operation with this key." Written in the SAME transaction as the money operation it
/// protects, so either both exist or neither does (a rejected / rolled-back request never burns its key).
/// Unique on (Scope, UserId, Key). Never holds money amounts itself: it points at the entity it created.
/// </summary>
public class IdempotencyRecord
{
    public int Id { get; set; }

    /// <summary>Which operation, e.g. "payment.create".</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Keys are private to the user who sent them.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The client's Idempotency-Key header.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Hex SHA-256 of the normalised request. The same key with a different payload is a conflict.</summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>What the first request created, e.g. "Payment".</summary>
    public string ResultEntityType { get; set; } = string.Empty;

    public int ResultEntityId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>After this moment the key is forgotten and may be reused (30 days).</summary>
    public DateTime ExpiresAt { get; set; }
}
