namespace ToolShop.Domain.Entities.Cash;

public enum CashShiftStatus
{
    Open = 1,
    Closed = 2
}

/// <summary>
/// One cashier's shift on one cash drawer. At most ONE open shift per drawer (enforced by a partial unique index in the
/// database, not only in code). Every figure that can be derived from the ledgers (expected cash, card totals, ...) is
/// COMPUTED; only the values that are decisions at closing time are stored: the counted cash, and a snapshot of the
/// expected cash and the difference so that a closed shift never changes afterwards.
/// </summary>
public class CashShift
{
    public int Id { get; set; }

    /// <summary>SHF-000001 ... from cash_shift_number_seq.</summary>
    public string ShiftNumber { get; set; } = string.Empty;

    public int CashRegisterId { get; set; }
    public CashRegister CashRegister { get; set; } = null!;

    /// <summary>The cashier responsible for the cash of this shift.</summary>
    public string CashierUserId { get; set; } = string.Empty;

    public string OpenedByUserId { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Cash in the drawer when the shift started (counted by the cashier).</summary>
    public decimal OpeningCash { get; set; }

    public CashShiftStatus Status { get; set; } = CashShiftStatus.Open;

    // ---- filled when the shift is closed; null (N/A) while it is open ----
    public DateTime? ClosedAt { get; set; }
    public string? ClosedByUserId { get; set; }

    /// <summary>Actual cash counted at closing.</summary>
    public decimal? ClosingCash { get; set; }

    /// <summary>Snapshot of the expected cash at the moment of closing.</summary>
    public decimal? ExpectedCashAtClose { get; set; }

    /// <summary>ClosingCash - ExpectedCashAtClose (negative = shortage, positive = surplus).</summary>
    public decimal? CashDifference { get; set; }

    public string? CloseNote { get; set; }

    /// <summary>True when somebody other than the cashier closed it (manager / admin).</summary>
    public bool ClosedByOther { get; set; }

    /// <summary>A non-zero difference must be reviewed by a manager / admin.</summary>
    public bool RequiresReview { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? ReviewNote { get; set; }

    public List<CashMovement> Movements { get; set; } = [];
}
