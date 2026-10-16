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

/// <summary>The ONE place that says which order statuses count where (replaces scattered "Status == Completed" filters).</summary>
public static class SalesFilters
{
    /// <summary>Orders that were really sold: they stay in the revenue history of their sale period (returns are booked separately).</summary>
    public static readonly OrderStatus[] Sold = [OrderStatus.Completed, OrderStatus.PartiallyReturned, OrderStatus.Returned];

    /// <summary>Orders that can still carry a customer debt and accept payments.</summary>
    public static readonly OrderStatus[] Receivable = [OrderStatus.Completed, OrderStatus.PartiallyReturned];
}

public static class OrderBalance
{
    /// <summary>Set-based version of <see cref="LoadAsync"/> for many orders (3 queries total, not 3 per order).</summary>
    public static async Task<Dictionary<int, OrderBalanceSnapshot>> LoadManyAsync(
        AppDbContext db, IReadOnlyCollection<(int Id, decimal Total)> orders)
    {
        var ids = orders.Select(o => o.Id).ToList();

        var paid = await db.Payments.AsNoTracking()
            .Where(p => ids.Contains(p.OrderId) && p.Status == PaymentStatus.Completed)
            .GroupBy(p => p.OrderId)
            .Select(g => new { OrderId = g.Key, Sum = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.OrderId, x => x.Sum);

        var returned = await db.OrderReturns.AsNoTracking()
            .Where(r => ids.Contains(r.OrderId))
            .GroupBy(r => r.OrderId)
            .Select(g => new { OrderId = g.Key, Goods = g.Sum(r => r.GoodsValue), Refund = g.Sum(r => r.RefundAmount) })
            .ToDictionaryAsync(x => x.OrderId);

        var result = new Dictionary<int, OrderBalanceSnapshot>(orders.Count);
        foreach (var (id, total) in orders)
        {
            returned.TryGetValue(id, out var r);
            result[id] = new OrderBalanceSnapshot(total, r?.Goods ?? 0m, paid.GetValueOrDefault(id, 0m), r?.Refund ?? 0m);
        }
        return result;
    }

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
