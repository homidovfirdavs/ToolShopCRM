using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;

namespace ToolShop.Domain.Entities.Cash;

/// <summary>
/// Which shift received a payment. A separate table on purpose: the Payments table is not changed, so the money logic that
/// already works is untouched. A payment taken while no shift was open (or before the feature existed) has no row here.
/// </summary>
public class CashShiftPayment
{
    public int PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;

    public int CashShiftId { get; set; }
    public CashShift CashShift { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Which shift paid a refund of a return out of its drawer.</summary>
public class CashShiftReturn
{
    public int OrderReturnId { get; set; }
    public OrderReturn OrderReturn { get; set; } = null!;

    public int CashShiftId { get; set; }
    public CashShift CashShift { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
