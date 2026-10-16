using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Infrastructure.Persistence;
using ToolShop.Infrastructure.Sales;

namespace ToolShop.Infrastructure.Finance;

public record FinanceSummaryResult(
    decimal Revenue, decimal Cogs, decimal GrossProfit, decimal OperatingExpenses,
    decimal NetOperatingProfit, decimal PaymentsReceived, decimal AccountsReceivable,
    decimal PurchaseSpend, int CompletedOrdersCount, decimal AverageOrderValue,
    decimal GrossMarginPercent, decimal NetOperatingMarginPercent, int UnknownCostItemsCount,
    decimal ReturnsValue, decimal RefundsPaid, decimal WriteOffCost);

public record DailyFinanceResult(
    DateOnly Date, decimal Revenue, decimal Cogs, decimal GrossProfit,
    decimal Expenses, decimal NetOperatingProfit, decimal PaymentsReceived, bool HasCompleteCostData);

public record MonthlyFinanceResult(
    int Year, int Month, decimal Revenue, decimal Cogs, decimal GrossProfit,
    decimal Expenses, decimal NetOperatingProfit, decimal PaymentsReceived, decimal PurchaseSpend);
public record ExpenseCategoryBreakdownResult(string Category, decimal Amount, decimal Percentage);
public record PaymentMethodBreakdownResult(string Method, decimal Amount, int Count);
public record ProfitByProductResult(
    int ProductId, string ProductName, string Sku, int QuantitySold,
    decimal Revenue, decimal? Cogs, decimal? GrossProfit, decimal? GrossMarginPercent,
    bool HasCompleteCostData);
public record ProfitByCategoryResult(
    string Category, int QuantitySold, decimal Revenue, decimal? Cogs,
    decimal? GrossProfit, decimal? MarginPercent, bool HasCompleteCostData);
public class FinanceService
{
    private readonly AppDbContext _db;

    public FinanceService(AppDbContext db)
    {
        _db = db;
    }

    private static DateTime ToUtc(DateTime d) => DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);

    // TASK C3 - returns are booked in the period of the RETURN, the sale stays in the period of the SALE:
    //   Revenue_P  = sum(Total of sold orders by OrderDate) - sum(GoodsValue of returns by ReturnedAt)       (may be negative)
    //   COGS_P     = sum(qty x CostAtSale of those sales) - sum(RestockedQty x CostAtSale of returns by ReturnedAt)
    //                (written-off units stay in COGS; their cost is only reported as WriteOffCost, never added or removed again)
    //   Payments_P = sum(valid payments by CreatedAt) - sum(RefundAmount of returns by ReturnedAt)
    //   AR (point in time) = sum over sold orders of max(0, Total - returned goods - (paid - refunded))
    // Unknown cost (null) is never treated as 0: it is counted in UnknownCostItemsCount so the UI shows N/A.
    public async Task<FinanceSummaryResult> GetSummaryAsync(DateTime from, DateTime to)
    {
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to).AddDays(1);

        var soldOrdersQuery = _db.Orders.AsNoTracking()
            .Where(o => SalesFilters.Sold.Contains(o.Status) && o.OrderDate >= fromUtc && o.OrderDate < toUtc);

        var grossSales = await soldOrdersQuery.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
        var soldOrdersCount = await soldOrdersQuery.CountAsync();

        var returnsQuery = _db.OrderReturns.AsNoTracking()
            .Where(r => r.ReturnedAt >= fromUtc && r.ReturnedAt < toUtc);
        var returnsValue = await returnsQuery.SumAsync(r => (decimal?)r.GoodsValue) ?? 0m;
        var refundsPaid = await returnsQuery.SumAsync(r => (decimal?)r.RefundAmount) ?? 0m;

        var revenue = grossSales - returnsValue;

        var itemsQuery = _db.OrderItems.AsNoTracking()
            .Where(i => SalesFilters.Sold.Contains(i.Order.Status)
                     && i.Order.OrderDate >= fromUtc && i.Order.OrderDate < toUtc);

        var saleCogs = await itemsQuery
            .Where(i => i.CostAtSale != null)
            .SumAsync(i => (decimal?)(i.Quantity * i.CostAtSale!.Value)) ?? 0m;

        var returnItemsQuery = _db.OrderReturnItems.AsNoTracking()
            .Where(i => i.OrderReturn.ReturnedAt >= fromUtc && i.OrderReturn.ReturnedAt < toUtc);

        var cogsReversed = await returnItemsQuery
            .Where(i => i.RestockedQuantity > 0 && i.CostAtSaleSnapshot != null)
            .SumAsync(i => (decimal?)(i.RestockedQuantity * i.CostAtSaleSnapshot!.Value)) ?? 0m;
        var writeOffCost = await returnItemsQuery
            .Where(i => i.WrittenOffQuantity > 0 && i.CostAtSaleSnapshot != null)
            .SumAsync(i => (decimal?)(i.WrittenOffQuantity * i.CostAtSaleSnapshot!.Value)) ?? 0m;

        var cogs = saleCogs - cogsReversed;

        var unknownCostItemsCount = await itemsQuery.CountAsync(i => i.CostAtSale == null)
            // a restocked unit with unknown cost whose SALE is in another period still makes THIS period's COGS incomplete
            + await returnItemsQuery.CountAsync(i => i.RestockedQuantity > 0 && i.CostAtSaleSnapshot == null
                && (i.OrderItem.Order.OrderDate < fromUtc || i.OrderItem.Order.OrderDate >= toUtc));

        var operatingExpenses = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsVoided && e.ExpenseDate >= fromUtc && e.ExpenseDate < toUtc)
            .SumAsync(e => (decimal?)e.Amount) ?? 0m;

        var paymentsIn = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Completed && p.CreatedAt >= fromUtc && p.CreatedAt < toUtc)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        var paymentsReceived = paymentsIn - refundsPaid;

        var purchaseSpend = await _db.Purchases.AsNoTracking()
            .Where(p => p.Status == PurchaseStatus.Confirmed && p.PurchaseDate >= fromUtc && p.PurchaseDate < toUtc)
            .SumAsync(p => (decimal?)p.TotalAmount) ?? 0m;

        // Accounts Receivable is a point-in-time balance, not limited to the selected period.
        var allSoldOrders = await _db.Orders.AsNoTracking()
            .Where(o => SalesFilters.Receivable.Contains(o.Status))
            .Select(o => new { o.Id, o.TotalAmount })
            .ToListAsync();
        var balances = await OrderBalance.LoadManyAsync(_db, allSoldOrders.Select(o => (o.Id, o.TotalAmount)).ToList());
        var accountsReceivable = balances.Values.Sum(b => b.Outstanding);

        var grossProfit = revenue - cogs;
        var netOperatingProfit = grossProfit - operatingExpenses;
        var averageOrderValue = soldOrdersCount == 0 ? 0m : grossSales / soldOrdersCount;
        // A margin on zero or negative revenue is meaningless, so it is reported as 0 (the amounts themselves stay negative).
        var grossMarginPercent = revenue <= 0 ? 0m : Math.Round(grossProfit / revenue * 100, 2);
        var netMarginPercent = revenue <= 0 ? 0m : Math.Round(netOperatingProfit / revenue * 100, 2);

        return new FinanceSummaryResult(
            revenue, cogs, grossProfit, operatingExpenses, netOperatingProfit,
            paymentsReceived, accountsReceivable, purchaseSpend, soldOrdersCount,
            averageOrderValue, grossMarginPercent, netMarginPercent, unknownCostItemsCount,
            returnsValue, refundsPaid, writeOffCost);
    }

    public async Task<List<DailyFinanceResult>> GetDailyAsync(DateTime from, DateTime to)
    {
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to).AddDays(1);

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => SalesFilters.Sold.Contains(o.Status) && o.OrderDate >= fromUtc && o.OrderDate < toUtc)
            .Select(o => new { o.Id, o.OrderDate, o.TotalAmount })
            .ToListAsync();

        var items = await _db.OrderItems.AsNoTracking()
            .Where(i => SalesFilters.Sold.Contains(i.Order.Status)
                     && i.Order.OrderDate >= fromUtc && i.Order.OrderDate < toUtc)
            .Select(i => new { i.Order.OrderDate, i.Quantity, i.CostAtSale })
            .ToListAsync();

        var returns = await _db.OrderReturns.AsNoTracking()
            .Where(r => r.ReturnedAt >= fromUtc && r.ReturnedAt < toUtc)
            .Select(r => new { r.ReturnedAt, r.GoodsValue, r.RefundAmount })
            .ToListAsync();

        var returnItems = await _db.OrderReturnItems.AsNoTracking()
            .Where(i => i.OrderReturn.ReturnedAt >= fromUtc && i.OrderReturn.ReturnedAt < toUtc)
            .Select(i => new { i.OrderReturn.ReturnedAt, i.RestockedQuantity, i.CostAtSaleSnapshot })
            .ToListAsync();

        var expenses = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsVoided && e.ExpenseDate >= fromUtc && e.ExpenseDate < toUtc)
            .Select(e => new { e.ExpenseDate, e.Amount })
            .ToListAsync();

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Completed && p.CreatedAt >= fromUtc && p.CreatedAt < toUtc)
            .Select(p => new { p.CreatedAt, p.Amount })
            .ToListAsync();

        var days = new List<DailyFinanceResult>();
        for (var d = fromUtc.Date; d < toUtc; d = d.AddDays(1))
        {
            var dateOnly = DateOnly.FromDateTime(d);
            var dayItems = items.Where(i => i.OrderDate.Date == d).ToList();
            var dayReturns = returns.Where(r => r.ReturnedAt.Date == d).ToList();
            var dayReturnItems = returnItems.Where(r => r.ReturnedAt.Date == d && r.RestockedQuantity > 0).ToList();

            var dayRevenue = orders.Where(o => o.OrderDate.Date == d).Sum(o => o.TotalAmount) - dayReturns.Sum(r => r.GoodsValue);
            var dayCogs = dayItems.Where(i => i.CostAtSale != null).Sum(i => i.Quantity * i.CostAtSale!.Value)
                - dayReturnItems.Where(i => i.CostAtSaleSnapshot != null).Sum(i => i.RestockedQuantity * i.CostAtSaleSnapshot!.Value);
            var dayExpenses = expenses.Where(e => e.ExpenseDate.Date == d).Sum(e => e.Amount);
            var dayPayments = payments.Where(p => p.CreatedAt.Date == d).Sum(p => p.Amount) - dayReturns.Sum(r => r.RefundAmount);
            var dayGrossProfit = dayRevenue - dayCogs;
            // A day's gross profit is only trustworthy when every sold / restocked item that day has a known cost snapshot.
            var hasCompleteCostData = dayItems.All(i => i.CostAtSale != null)
                && dayReturnItems.All(i => i.CostAtSaleSnapshot != null);

            days.Add(new DailyFinanceResult(
                dateOnly, dayRevenue, dayCogs, dayGrossProfit,
                dayExpenses, dayGrossProfit - dayExpenses, dayPayments, hasCompleteCostData));
        }

        return days;
    }

    public async Task<List<MonthlyFinanceResult>> GetMonthlyAsync(DateTime from, DateTime to)
    {
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to).AddDays(1);

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => SalesFilters.Sold.Contains(o.Status) && o.OrderDate >= fromUtc && o.OrderDate < toUtc)
            .Select(o => new { o.OrderDate, o.TotalAmount })
            .ToListAsync();

        var items = await _db.OrderItems.AsNoTracking()
            .Where(i => SalesFilters.Sold.Contains(i.Order.Status)
                     && i.Order.OrderDate >= fromUtc && i.Order.OrderDate < toUtc)
            .Select(i => new { i.Order.OrderDate, i.Quantity, i.CostAtSale })
            .ToListAsync();

        var returns = await _db.OrderReturns.AsNoTracking()
            .Where(r => r.ReturnedAt >= fromUtc && r.ReturnedAt < toUtc)
            .Select(r => new { r.ReturnedAt, r.GoodsValue, r.RefundAmount })
            .ToListAsync();

        var returnItems = await _db.OrderReturnItems.AsNoTracking()
            .Where(i => i.OrderReturn.ReturnedAt >= fromUtc && i.OrderReturn.ReturnedAt < toUtc && i.RestockedQuantity > 0)
            .Select(i => new { i.OrderReturn.ReturnedAt, i.RestockedQuantity, i.CostAtSaleSnapshot })
            .ToListAsync();

        var expenses = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsVoided && e.ExpenseDate >= fromUtc && e.ExpenseDate < toUtc)
            .Select(e => new { e.ExpenseDate, e.Amount })
            .ToListAsync();

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Completed && p.CreatedAt >= fromUtc && p.CreatedAt < toUtc)
            .Select(p => new { p.CreatedAt, p.Amount })
            .ToListAsync();

        var purchases = await _db.Purchases.AsNoTracking()
            .Where(p => p.Status == PurchaseStatus.Confirmed && p.PurchaseDate >= fromUtc && p.PurchaseDate < toUtc)
            .Select(p => new { p.PurchaseDate, p.TotalAmount })
            .ToListAsync();

        var months = orders.Select(o => new { o.OrderDate.Year, o.OrderDate.Month })
            .Concat(returns.Select(r => new { r.ReturnedAt.Year, r.ReturnedAt.Month }))
            .Concat(expenses.Select(e => new { e.ExpenseDate.Year, e.ExpenseDate.Month }))
            .Concat(purchases.Select(p => new { p.PurchaseDate.Year, p.PurchaseDate.Month }))
            .Distinct()
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToList();

        var result = new List<MonthlyFinanceResult>();
        foreach (var m in months)
        {
            var monthReturns = returns.Where(r => r.ReturnedAt.Year == m.Year && r.ReturnedAt.Month == m.Month).ToList();
            var monthRevenue = orders.Where(o => o.OrderDate.Year == m.Year && o.OrderDate.Month == m.Month).Sum(o => o.TotalAmount)
                - monthReturns.Sum(r => r.GoodsValue);
            var monthCogs = items.Where(i => i.OrderDate.Year == m.Year && i.OrderDate.Month == m.Month && i.CostAtSale != null)
                    .Sum(i => i.Quantity * i.CostAtSale!.Value)
                - returnItems.Where(i => i.ReturnedAt.Year == m.Year && i.ReturnedAt.Month == m.Month && i.CostAtSaleSnapshot != null)
                    .Sum(i => i.RestockedQuantity * i.CostAtSaleSnapshot!.Value);
            var monthExpenses = expenses.Where(e => e.ExpenseDate.Year == m.Year && e.ExpenseDate.Month == m.Month).Sum(e => e.Amount);
            var monthPayments = payments.Where(p => p.CreatedAt.Year == m.Year && p.CreatedAt.Month == m.Month).Sum(p => p.Amount)
                - monthReturns.Sum(r => r.RefundAmount);
            var monthPurchaseSpend = purchases.Where(p => p.PurchaseDate.Year == m.Year && p.PurchaseDate.Month == m.Month).Sum(p => p.TotalAmount);
            var monthGrossProfit = monthRevenue - monthCogs;

            result.Add(new MonthlyFinanceResult(
                m.Year, m.Month, monthRevenue, monthCogs, monthGrossProfit,
                monthExpenses, monthGrossProfit - monthExpenses, monthPayments, monthPurchaseSpend));
        }

        return result;
    }
    
    public async Task<List<ExpenseCategoryBreakdownResult>> GetExpensesByCategoryAsync(DateTime from, DateTime to)
    {
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to).AddDays(1);

        var raw = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsVoided && e.ExpenseDate >= fromUtc && e.ExpenseDate < toUtc)
            .GroupBy(e => e.ExpenseCategory.Name)
            .Select(g => new { Category = g.Key, Amount = g.Sum(e => e.Amount) })
            .ToListAsync();

        var total = raw.Sum(x => x.Amount);

        return raw
            .OrderByDescending(x => x.Amount)
            .Select(x => new ExpenseCategoryBreakdownResult(
                x.Category, x.Amount, total == 0 ? 0m : Math.Round(x.Amount / total * 100, 2)))
            .ToList();
    }

    public async Task<List<PaymentMethodBreakdownResult>> GetPaymentsByMethodAsync(DateTime from, DateTime to)
    {
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to).AddDays(1);

        var raw = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Completed && p.CreatedAt >= fromUtc && p.CreatedAt < toUtc)
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(p => p.Amount), Count = g.Count() })
            .ToListAsync();

        // Money paid back to customers in this period leaves through the method it was refunded with.
        var refunds = await _db.OrderReturns.AsNoTracking()
            .Where(r => r.ReturnedAt >= fromUtc && r.ReturnedAt < toUtc && r.RefundAmount > 0 && r.RefundMethod != null)
            .GroupBy(r => r.RefundMethod)
            .Select(g => new { Method = g.Key!.Value, Amount = g.Sum(r => r.RefundAmount) })
            .ToListAsync();

        var methods = raw.Select(x => x.Method).Union(refunds.Select(x => x.Method)).Distinct();

        return methods
            .Select(m => new PaymentMethodBreakdownResult(
                m.ToString(),
                raw.Where(x => x.Method == m).Sum(x => x.Amount) - refunds.Where(x => x.Method == m).Sum(x => x.Amount),
                raw.Where(x => x.Method == m).Sum(x => x.Count)))
            .OrderByDescending(x => x.Amount)
            .ToList();
    }

    /// <summary>One line of a product/category profit table: a sold order line (+) or a returned line (-).</summary>
    private sealed record ProfitLine(
        int ProductId, string ProductName, string Sku, string Category,
        int Quantity, decimal Revenue, decimal Cogs, bool CostKnown);

    private async Task<List<ProfitLine>> LoadProfitLinesAsync(DateTime fromUtc, DateTime toUtc)
    {
        var rows = await _db.OrderItems.AsNoTracking()
            .Where(i => SalesFilters.Sold.Contains(i.Order.Status)
                     && i.Order.OrderDate >= fromUtc && i.Order.OrderDate < toUtc)
            .Select(i => new
            {
                i.OrderId,
                OrderTotal = i.Order.TotalAmount,
                i.ProductId,
                ProductName = i.Product.Name,
                Sku = i.Product.SKU,
                Category = i.Product.Category.Name,
                i.Quantity,
                Line = (i.Quantity * i.UnitPrice) - i.DiscountAmount,
                i.CostAtSale
            })
            .ToListAsync();

        // Order-level discount is allocated pro-rata across the order's items,
        // so product/category revenue adds up to Order.TotalAmount (same as Summary).
        var lineSumByOrder = rows.GroupBy(r => r.OrderId).ToDictionary(g => g.Key, g => g.Sum(r => r.Line));

        var lines = rows.Select(r =>
        {
            var sum = lineSumByOrder[r.OrderId];
            var revenue = sum <= 0 ? 0m : r.Line * (r.OrderTotal / sum);
            return new ProfitLine(r.ProductId, r.ProductName, r.Sku, r.Category, r.Quantity, revenue,
                r.CostAtSale is null ? 0m : r.Quantity * r.CostAtSale.Value, r.CostAtSale != null);
        }).ToList();

        // Returns count in the period of the RETURN: revenue of the returned value goes out, only RESTOCKED units leave COGS.
        var returned = await _db.OrderReturnItems.AsNoTracking()
            .Where(i => i.OrderReturn.ReturnedAt >= fromUtc && i.OrderReturn.ReturnedAt < toUtc)
            .Select(i => new
            {
                i.ProductId,
                ProductName = i.Product.Name,
                Sku = i.Product.SKU,
                Category = i.Product.Category.Name,
                i.Quantity,
                i.NetValue,
                i.RestockedQuantity,
                i.CostAtSaleSnapshot
            })
            .ToListAsync();

        lines.AddRange(returned.Select(r => new ProfitLine(
            r.ProductId, r.ProductName, r.Sku, r.Category, -r.Quantity, -r.NetValue,
            r.RestockedQuantity > 0 && r.CostAtSaleSnapshot != null ? -(r.RestockedQuantity * r.CostAtSaleSnapshot.Value) : 0m,
            r.RestockedQuantity == 0 || r.CostAtSaleSnapshot != null)));

        return lines;
    }

    public async Task<(List<ProfitByProductResult> Items, int TotalItems)> GetProfitByProductAsync(
        DateTime from, DateTime to, string sortBy, int page, int pageSize)
    {
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to).AddDays(1);

        var raw = (await LoadProfitLinesAsync(fromUtc, toUtc))
            .GroupBy(r => new { r.ProductId, r.ProductName, r.Sku })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                g.Key.Sku,
                QuantitySold = g.Sum(r => r.Quantity),
                Revenue = Math.Round(g.Sum(r => r.Revenue), 2),
                Cogs = g.Sum(r => r.Cogs),
                HasCompleteCostData = g.All(r => r.CostKnown)
            })
            .ToList();

        var items = raw.Select(x =>
        {
            var grossProfit = x.Revenue - x.Cogs;
            var marginPercent = x.Revenue <= 0 ? 0m : Math.Round(grossProfit / x.Revenue * 100, 2);

            return new ProfitByProductResult(
                x.ProductId, x.ProductName, x.Sku, x.QuantitySold, x.Revenue,
                x.HasCompleteCostData ? x.Cogs : null,
                x.HasCompleteCostData ? grossProfit : null,
                x.HasCompleteCostData ? marginPercent : null,
                x.HasCompleteCostData);
        }).ToList();

        // Rows with incomplete cost data sort by revenue as a fallback, since their margin/profit is unknown, not zero.
        items = sortBy switch
        {
            "revenue" => items.OrderByDescending(x => x.Revenue).ToList(),
            "margin" => items.OrderByDescending(x => x.GrossMarginPercent ?? -1m).ToList(),
            "margin_asc" => items.OrderBy(x => x.GrossMarginPercent ?? decimal.MaxValue).ToList(),
            _ => items.OrderByDescending(x => x.GrossProfit ?? x.Revenue).ToList()
        };

        var totalItems = items.Count;
        var paged = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return (paged, totalItems);
    }

    public async Task<List<ProfitByCategoryResult>> GetProfitByCategoryAsync(DateTime from, DateTime to)
    {
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to).AddDays(1);

        var raw = (await LoadProfitLinesAsync(fromUtc, toUtc))
            .GroupBy(r => r.Category)
            .Select(g => new
            {
                Category = g.Key,
                QuantitySold = g.Sum(r => r.Quantity),
                Revenue = Math.Round(g.Sum(r => r.Revenue), 2),
                Cogs = g.Sum(r => r.Cogs),
                HasCompleteCostData = g.All(r => r.CostKnown)
            })
            .ToList();

        return raw.Select(x =>
        {
            var grossProfit = x.Revenue - x.Cogs;
            var marginPercent = x.Revenue <= 0 ? 0m : Math.Round(grossProfit / x.Revenue * 100, 2);

            return new ProfitByCategoryResult(
                x.Category, x.QuantitySold, x.Revenue,
                x.HasCompleteCostData ? x.Cogs : null,
                x.HasCompleteCostData ? grossProfit : null,
                x.HasCompleteCostData ? marginPercent : null,
                x.HasCompleteCostData);
        }).OrderByDescending(x => x.GrossProfit ?? x.Revenue).ToList();
    }

}