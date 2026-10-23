using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Cash;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Cash;

/// <summary>
/// Called INSIDE the transaction that records a payment / a refund: remembers which shift took the money. It only adds a
/// row to a separate link table; the payment / return itself is not changed. Does nothing while the feature is off.
/// </summary>
public static class CashShiftLinker
{
    private static async Task<int?> OpenShiftIdAsync(AppDbContext db, CashShiftOptions options)
    {
        var id = await db.CashShifts.AsNoTracking()
            .Where(s => s.Status == CashShiftStatus.Open)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync();

        if (id is null && options.RequireOpenShift)
        {
            throw new NoOpenCashShiftException();
        }

        return id;
    }

    /// <param name="payment">Already added to the context (tracked) but not necessarily saved yet.</param>
    public static async Task AttachPaymentAsync(AppDbContext db, CashShiftOptions options, Payment payment)
    {
        if (!options.Enabled) return;

        var shiftId = await OpenShiftIdAsync(db, options);
        if (shiftId is null) return;

        db.CashShiftPayments.Add(new CashShiftPayment { Payment = payment, CashShiftId = shiftId.Value });
    }

    /// <summary>Only a return that really pays money back needs a shift.</summary>
    public static async Task AttachReturnAsync(AppDbContext db, CashShiftOptions options, OrderReturn ledger)
    {
        if (!options.Enabled || ledger.RefundAmount <= 0m) return;

        var shiftId = await OpenShiftIdAsync(db, options);
        if (shiftId is null) return;

        db.CashShiftReturns.Add(new CashShiftReturn { OrderReturn = ledger, CashShiftId = shiftId.Value });
    }
}
