using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Sales;

/// <summary>
/// Money position of ONE order after returns (nothing here is stored - it is always derived):
///   NetTotal    = Total - value of returned goods
///   NetPaid     = valid (Completed) payments - money actually refunded
///   Outstanding = max(0, NetTotal - NetPaid)      (debt the customer still owes)
///   Overpaid    = max(0, NetPaid - NetTotal)      (money we owe back; the upper limit of a refund)
/// </summary>
public record OrderBalanceSnapshot(decimal Total, decimal ReturnedValue, decimal Paid, decimal Refunded)
{
    public decimal NetTotal => Total - ReturnedValue;
    public decimal NetPaid => Paid - Refunded;
    public decimal Outstanding => Math.Max(0m, NetTotal - NetPaid);
    public decimal Overpaid => Math.Max(0m, NetPaid - NetTotal);
}

public static class OrderBalance
{
    public static async Task<OrderBalanceSnapshot> LoadAsync(AppDbContext db, int orderId, decimal orderTotal)
    {
        var paid = await db.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var returns = await db.OrderReturns.AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .Select(r => new { r.GoodsValue, r.RefundAmount })
            .ToListAsync();

        return new OrderBalanceSnapshot(orderTotal, returns.Sum(r => r.GoodsValue), paid, returns.Sum(r => r.RefundAmount));
    }
}

public static class ReturnNumberGenerator
{
    public static async Task<string> GenerateAsync(AppDbContext db)
    {
        var result = await db.Database
            .SqlQueryRaw<long>("SELECT nextval('return_number_seq') AS \"Value\"")
            .SingleAsync();

        return $"RET-{result:D6}";
    }
}
