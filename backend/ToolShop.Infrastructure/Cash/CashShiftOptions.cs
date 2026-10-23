namespace ToolShop.Infrastructure.Cash;

/// <summary>
/// Cash-shift switches (configuration section "CashShifts"). Everything is OFF by default, so the application behaves
/// exactly as before until the database migration has been applied and the feature is switched on on purpose.
/// </summary>
public class CashShiftOptions
{
    /// <summary>Master switch. While false no cash-shift table is read or written.</summary>
    public bool Enabled { get; set; }

    /// <summary>When true, taking a payment or paying out a refund needs an open shift (otherwise 409 NO_OPEN_CASH_SHIFT).</summary>
    public bool RequireOpenShift { get; set; }

    /// <summary>The most a CASHIER may put in / take out of the drawer by hand in one movement. Manager / admin: no such limit.</summary>
    public decimal CashierMovementLimit { get; set; } = 100m;

    /// <summary>Upper bound for any single amount (typo guard).</summary>
    public decimal MaxAmount { get; set; } = 10_000_000m;
}
