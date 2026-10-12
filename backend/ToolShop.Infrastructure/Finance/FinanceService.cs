using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Finance;

public record FinanceSummaryResult(
    decimal Revenue, decimal Cogs, decimal GrossProfit, decimal OperatingExpenses,
    decimal NetOperatingProfit, decimal PaymentsReceived, decimal AccountsReceivable,
    decimal PurchaseSpend, int CompletedOrdersCount, decimal AverageOrderValue,
    decimal GrossMarginPercent, decimal NetOperatingMarginPercent, int UnknownCostItemsCount);

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

    public async Task<FinanceSummaryResult> GetSummaryAsync(DateTime from, DateTime to)
    {
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to).AddDays(1);

        var completedOrdersQuery = _db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.OrderDate >= fromUtc && o.OrderDate < toUtc);

        var revenue = await completedOrdersQuery.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
        var completedOrdersCount = await completedOrdersQuery.CountAsync();

        var itemsQuery = _db.OrderItems.AsNoTracking()
            .Where(i => i.Order.Status == OrderStatus.Completed
                     && i.Order.OrderDate >= fromUtc && i.Order.OrderDate < toUtc);

        var cogs = await itemsQuery
            .Where(i => i.CostAtSale != null)
            .SumAsync(i => (decimal?)(i.Quantity * i.CostAtSale!.Value)) ?? 0m;

        var unknownCostItemsCount = await itemsQuery.CountAsync(i => i.CostAtSale == null);

        var operatingExpenses = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsVoided && e.ExpenseDate >= fromUtc && e.ExpenseDate < toUtc)
            .SumAsync(e => (decimal?)e.Amount) ?? 0m;

        var paymentsReceived = await _db.Payments.AsNoTracking()            .Where(p => p.Status == PaymentStatus.Completed && p.CreatedAt >= fromUtc && p.CreatedAt < toUtc)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var purchaseSpend = await _db.Purchases.AsNoTracking()
            .Where(p => p.Status == PurchaseStatus.Confirmed && p.PurchaseDate >= fromUtc && p.PurchaseDate < toUtc)
            .SumAsync(p => (decimal?)p.TotalAmount) ?? 0m;

        // Accounts Receivable is a point-in-time balance, not limited to the selected period.
        var allCompletedOrders = await _db.Orders.AsNoTracking()            .Where(o => o.Status == OrderStatus.Completed)
            .Select(o => new { o.Id, o.TotalAmount })
            .ToListAsync();

        var paidByOrder = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Completed)
            .GroupBy(p => p.OrderId)
            .Select(g => new { OrderId = g.Key, Paid = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.OrderId, x => x.Paid);

        var accountsReceivable = allCompletedOrders.Sum(o =>
            Math.Max(0, o.TotalAmount - paidByOrder.GetValueOrDefault(o.Id, 0m)));

        var grossProfit = revenue - cogs;
        var netOperatingProfit = grossProfit - operatingExpenses;
        var averageOrderValue = completedOrdersCount == 0 ? 0m : revenue / completedOrdersCount;
        var grossMarginPercent = revenue == 0 ? 0m : Math.Round(grossProfit / revenue * 100, 2);
        var netMarginPercent = revenue == 0 ? 0m : Math.Round(netOperatingProfit / revenue * 100, 2);

        return new FinanceSummaryResult(
            revenue, cogs, grossProfit, operatingExpenses, netOperatingProfit,
            paymentsReceived, accountsReceivable, purchaseSpend, completedOrdersCount,
            averageOrderValue, grossMarginPercent, netMarginPercent, unknownCostItemsCount);
    }

       public async Task<List<DailyFinanceResult>> GetDailyAsync(DateTime from, DateTime to)
    {
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to).AddDays(1);

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.OrderDate >= fromUtc && o.OrderDate < toUtc)
            .Select(o => new { o.Id, o.OrderDate, o.TotalAmount })
            .ToListAsync();

        var items = await _db.OrderItems.AsNoTracking()
            .Where(i => i.Order.Status == OrderStatus.Completed
                     && i.Order.OrderDate >= fromUtc && i.Order.OrderDate < toUtc)
            .Select(i => new { i.Order.OrderDate, i.Quantity, i.CostAtSale })
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
            var dayRevenue = orders.Where(o => o.OrderDate.Date == d).Sum(o => o.TotalAmount);
            var dayCogs = dayItems.Where(i => i.CostAtSale != null).Sum(i => i.Quantity * i.CostAtSale!.Value);
            var dayExpenses = expenses.Where(e => e.ExpenseDate.Date == d).Sum(e => e.Amount);
            var dayPayments = payments.Where(p => p.CreatedAt.Date == d).Sum(p => p.Amount);
            var dayGrossProfit = dayRevenue - dayCogs;
            // A day's gross profit is only trustworthy when every sold item that day has a known cost snapshot.
            var hasCompleteCostData = dayItems.All(i => i.CostAtSale != null);

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
            .Where(o => o.Status == OrderStatus.Completed && o.OrderDate >= fromUtc && o.OrderDate < toUtc)
            .Select(o => new { o.OrderDate, o.TotalAmount })
            .ToListAsync();

        var items = await _db.OrderItems.AsNoTracking()
            .Where(i => i.Order.Status == OrderStatus.Completed
                     && i.Order.OrderDate >= fromUtc && i.Order.OrderDate < toUtc)
            .Select(i => new { i.Order.OrderDate, i.Quantity, i.CostAtSale })
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
            .Concat(expenses.Select(e => new { e.ExpenseDate.Year, e.ExpenseDate.Month }))
            .Concat(purchases.Select(p => new { p.PurchaseDate.Year, p.PurchaseDate.Month }))
            .Distinct()
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToList();

        var result = new List<MonthlyFinanceResult>();
        foreach (var m in months)
        {
            var monthRevenue = orders.Where(o => o.OrderDate.Year == m.Year && o.OrderDate.Month == m.Month).Sum(o => o.TotalAmount);
            var monthCogs = items.Where(i => i.OrderDate.Year == m.Year && i.OrderDate.Month == m.Month && i.CostAtSale != null)                .Sum(i => i.Quantity * i.CostAtSale!.Value);
            var monthExpenses = expenses.Where(e => e.ExpenseDate.Year == m.Year && e.ExpenseDate.Month == m.Month).Sum(e => e.Amount);
            var monthPayments = payments.Where(p => p.CreatedAt.Year == m.Year && p.CreatedAt.Month == m.Month).Sum(p => p.Amount);
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

        return raw
            .OrderByDescending(x => x.Amount)
            .Select(x => new PaymentMethodBreakdownResult(x.Method.ToString(), x.Amount, x.Count))
            .ToList();
    }


        public async Task<(List<ProfitByProductResult> Items, int TotalItems)> GetProfitByProductAsync(
        DateTime from, DateTime to, string sortBy, int page, int pageSize)
    {
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to).AddDays(1);

        var rows = await _db.OrderItems.AsNoTracking()
            .Where(i => i.Order.Status == OrderStatus.Completed
                     && i.Order.OrderDate >= fromUtc && i.Order.OrderDate < toUtc)
            .Select(i => new
            {
                i.OrderId,
                OrderTotal = i.Order.TotalAmount,
                i.ProductId,
                ProductName = i.Product.Name,
                Sku = i.Product.SKU,
                i.Quantity,
                Line = (i.Quantity * i.UnitPrice) - i.DiscountAmount,
                i.CostAtSale
            })
            .ToListAsync();

        // Order-level discount is allocated pro-rata across the order's items,
        // so product/category revenue adds up to Order.TotalAmount (same as Summary).
        var lineSumByOrder = rows.GroupBy(r => r.OrderId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Line));

        var allocated = rows.Select(r =>
        {
            var sum = lineSumByOrder[r.OrderId];
            var revenue = sum <= 0 ? 0m : r.Line * (r.OrderTotal / sum);
            return new { r.ProductId, r.ProductName, r.Sku, r.Quantity, Revenue = revenue, r.CostAtSale };
        }).ToList();

        var raw = allocated
            .GroupBy(r => new { r.ProductId, r.ProductName, r.Sku })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                g.Key.Sku,
                QuantitySold = g.Sum(r => r.Quantity),
                Revenue = Math.Round(g.Sum(r => r.Revenue), 2),
                Cogs = g.Where(r => r.CostAtSale != null).Sum(r => r.Quantity * r.CostAtSale!.Value),
                HasCompleteCostData = g.All(r => r.CostAtSale != null)
            })
            .ToList();

        var items = raw.Select(x =>
        {
            var grossProfit = x.Revenue - x.Cogs;
            var marginPercent = x.Revenue == 0 ? 0m : Math.Round(grossProfit / x.Revenue * 100, 2);

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

        var rows = await _db.OrderItems.AsNoTracking()
            .Where(i => i.Order.Status == OrderStatus.Completed
                     && i.Order.OrderDate >= fromUtc && i.Order.OrderDate < toUtc)
            .Select(i => new
            {
                i.OrderId,
                OrderTotal = i.Order.TotalAmount,
                Category = i.Product.Category.Name,
                i.Quantity,
                Line = (i.Quantity * i.UnitPrice) - i.DiscountAmount,
                i.CostAtSale
            })
            .ToListAsync();

        var lineSumByOrder = rows.GroupBy(r => r.OrderId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Line));

        var allocated = rows.Select(r =>
        {
            var sum = lineSumByOrder[r.OrderId];
            var revenue = sum <= 0 ? 0m : r.Line * (r.OrderTotal / sum);
            return new { r.Category, r.Quantity, Revenue = revenue, r.CostAtSale };
        }).ToList();

        var raw = allocated
            .GroupBy(r => r.Category)
            .Select(g => new
            {
                Category = g.Key,
                QuantitySold = g.Sum(r => r.Quantity),
                Revenue = Math.Round(g.Sum(r => r.Revenue), 2),
                Cogs = g.Where(r => r.CostAtSale != null).Sum(r => r.Quantity * r.CostAtSale!.Value),
                HasCompleteCostData = g.All(r => r.CostAtSale != null)
            })
            .ToList();

        return raw.Select(x =>
        {
            var grossProfit = x.Revenue - x.Cogs;
            var marginPercent = x.Revenue == 0 ? 0m : Math.Round(grossProfit / x.Revenue * 100, 2);

            return new ProfitByCategoryResult(
                x.Category, x.QuantitySold, x.Revenue,
                x.HasCompleteCostData ? x.Cogs : null,
                x.HasCompleteCostData ? grossProfit : null,
                x.HasCompleteCostData ? marginPercent : null,
                x.HasCompleteCostData);
        }).OrderByDescending(x => x.GrossProfit ?? x.Revenue).ToList();
    }

}