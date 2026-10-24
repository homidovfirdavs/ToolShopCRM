using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Cash;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Cash;

/// <summary>Every figure of one shift, derived from the ledgers. Nothing here is stored.</summary>
public record CashBreakdown(
    decimal OpeningCash,
    decimal CashSales,
    decimal CardSales,
    decimal BankTransferSales,
    decimal QrSales,
    decimal OtherSales,
    decimal CashPaymentsVoidedFromOtherShifts,
    decimal CashRefunds,
    decimal CashIn,
    decimal CashOut,
    decimal ExpectedCash,
    int PaymentCount,
    int RefundCount,
    int MovementCount);

public static class CashShiftCalculator
{
    /// <summary>
    /// Expected cash = opening
    ///   + cash payments received in this shift (Completed only; voided ones drop out by themselves)
    ///   - cash payments received outside this shift (earlier shift / before the feature) that were VOIDED during this shift
    ///   - cash refunds of returns paid out in this shift
    ///   + cash in - cash out (not voided).
    /// Card, bank transfer, QR and other payments are reported but never part of the expected CASH. Debt is not cash at all.
    /// </summary>
    public static async Task<CashBreakdown> ComputeAsync(AppDbContext db, CashShift shift)
    {
        var shiftId = shift.Id;
        var openedAt = shift.OpenedAt;
        DateTime? closedAt = shift.ClosedAt;

        var byMethod = await db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Completed
                        && db.CashShiftPayments.Any(l => l.PaymentId == p.Id && l.CashShiftId == shiftId))
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Sum = g.Sum(p => p.Amount), Count = g.Count() })
            .ToListAsync();

        decimal SumOf(PaymentMethod m) => byMethod.Where(x => x.Method == m).Sum(x => x.Sum);

        var cashSales = SumOf(PaymentMethod.Cash);

        var voidedElsewhere = await db.Payments.AsNoTracking()
            .Where(p => p.Method == PaymentMethod.Cash
                        && p.Status == PaymentStatus.Voided
                        && p.VoidedAt != null
                        && p.VoidedAt >= openedAt
                        && (closedAt == null || p.VoidedAt <= closedAt)
                        && !db.CashShiftPayments.Any(l => l.PaymentId == p.Id && l.CashShiftId == shiftId))
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var refunds = await db.OrderReturns.AsNoTracking()
            .Where(r => r.RefundMethod == PaymentMethod.Cash
                        && db.CashShiftReturns.Any(l => l.OrderReturnId == r.Id && l.CashShiftId == shiftId))
            .Select(r => r.RefundAmount)
            .ToListAsync();

        var moves = await db.CashMovements.AsNoTracking()
            .Where(m => m.CashShiftId == shiftId && !m.IsVoided)
            .Select(m => new { m.Type, m.Amount })
            .ToListAsync();

        var cashIn = moves.Where(m => m.Type == CashMovementType.CashIn).Sum(m => m.Amount);
        var cashOut = moves.Where(m => m.Type == CashMovementType.CashOut).Sum(m => m.Amount);
        var cashRefunds = refunds.Sum();

        var expected = shift.OpeningCash + cashSales - voidedElsewhere - cashRefunds + cashIn - cashOut;

        return new CashBreakdown(
            shift.OpeningCash,
            cashSales,
            SumOf(PaymentMethod.Card),
            SumOf(PaymentMethod.BankTransfer),
            SumOf(PaymentMethod.QR),
            SumOf(PaymentMethod.Other),
            voidedElsewhere,
            cashRefunds,
            cashIn,
            cashOut,
            expected,
            byMethod.Sum(x => x.Count),
            refunds.Count,
            moves.Count);
    }
}
