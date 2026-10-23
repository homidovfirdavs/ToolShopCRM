using ToolShop.Domain.Entities.Finance;

namespace ToolShop.Domain.Entities.Cash;

public enum CashMovementType
{
    CashIn = 1,
    CashOut = 2
}

/// <summary>
/// Money put into / taken out of the drawer by hand (float top-up, bank drop, paying a supplier or an expense in cash...).
/// A ledger row: it is voided (with a reason), never edited or deleted.
/// </summary>
public class CashMovement
{
    public int Id { get; set; }

    public int CashShiftId { get; set; }
    public CashShift CashShift { get; set; } = null!;

    public CashMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;

    /// <summary>Optional link to the expense this cash-out paid for.</summary>
    public int? ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsVoided { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}
