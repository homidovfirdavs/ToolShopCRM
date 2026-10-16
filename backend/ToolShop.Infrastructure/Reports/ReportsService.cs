using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Inventory;
using ToolShop.Infrastructure.Payments;
using ToolShop.Infrastructure.Persistence;
using ToolShop.Infrastructure.Sales;

namespace ToolShop.Infrastructure.Reports;

public record SalesReportRowResult(
    int OrderId, string OrderNumber, DateTime OrderDate, string? CustomerName,
    string CashierName, string Status, decimal Subtotal, decimal DiscountAmount,
    decimal TotalAmount, decimal TotalPaid, decimal OutstandingBalance, string PaymentStatus);

public record InventoryReportRowResult(
    int ProductId, string ProductName, string Sku, string CategoryName, string BrandName,
    int CurrentStock, int MinimumStockLevel, string StockStatus, decimal PurchasePrice,
    decimal SalePrice, decimal StockValue);

public record CustomerDebtReportRowResult(
    int CustomerId, string CustomerName, string Phone, int OutstandingOrderCount,
    decimal TotalDebt, DateTime? LastPurchaseDate);

public record PurchaseReportRowResult(
    int PurchaseId, string PurchaseNumber, DateTime PurchaseDate, string SupplierName,
    string Status, decimal Subtotal, decimal DiscountAmount, decimal TotalAmount,
    string CreatedByUserName, DateTime? ConfirmedAt);

public record ExpenseReportRowResult(
    int ExpenseId, string ExpenseNumber, DateTime ExpenseDate, string CategoryName,
    string Description, decimal Amount, string CreatedByUserName, bool IsVoided);

public record PaymentReportRowResult(
    int PaymentId, string PaymentNumber, DateTime CreatedAt, string OrderNumber,
    string CustomerName, decimal Amount, string Method, string Status, string ReceivedByUserName);

public record ReturnReportRowResult(
    int ReturnId, string ReturnNumber, DateTime ReturnedAt, int OrderId, string OrderNumber, string? CustomerName,
    string ReturnedByUserName, string Reason, int ReturnedUnits, int RestockedUnits, int WrittenOffUnits,
    decimal GoodsValue, decimal RefundAmount, string? RefundMethod, decimal? CostReversed, decimal? WriteOffCost);

/// <summary>Totals over ALL returns of the period. A cost total is null (N/A) when any of its rows has an unknown cost.</summary>
public record ReturnsReportTotalsResult(
    int ReturnsCount, decimal GoodsValue, decimal RefundsPaid, decimal? CostReversed, decimal? WriteOffCost);

public class ReportsService
{
    private readonly AppDbContext _db;
    private readonly InventoryService _inventoryService;
    private readonly PaymentService _paymentService;

    public ReportsService(AppDbContext db, InventoryService inventoryService, PaymentService paymentService)
    {
        _db = db;
        _inventoryService = inventoryService;
        _paymentService = paymentService;
    }

    // ===================== SALES REPORT =====================

    public async Task<(List<SalesReportRowResult> Items, int TotalCount)> GetSalesReportAsync(
        DateTime from, DateTime to, int? customerId, OrderStatus? status,
        string? cashierUserId, string? paymentStatus, string? search,
        int page, int pageSize)
    {
        var rows = await GetSalesReportRowsAsync(from, to, customerId, status, cashierUserId, paymentStatus, search);
        var total = rows.Count;
        var pageItems = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (pageItems, total);
    }

    public async Task<List<SalesReportRowResult>> GetSalesReportForExportAsync(
        DateTime from, DateTime to, int? customerId, OrderStatus? status,
        string? cashierUserId, string? paymentStatus, string? search)
    {
        return await GetSalesReportRowsAsync(from, to, customerId, status, cashierUserId, paymentStatus, search);
    }

    private async Task<List<SalesReportRowResult>> GetSalesReportRowsAsync(
        DateTime from, DateTime to, int? customerId, OrderStatus? status,
        string? cashierUserId, string? paymentStatus, string? search)
    {
        var query = _db.Orders.AsNoTracking()
            .Where(o => o.OrderDate >= from && o.OrderDate <= to);

        if (customerId.HasValue)
            query = query.Where(o => o.CustomerId == customerId);

        if (status.HasValue)
            query = query.Where(o => o.Status == status);

        if (!string.IsNullOrWhiteSpace(cashierUserId))
            query = query.Where(o => o.CreatedByUserId == cashierUserId);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o => o.OrderNumber.Contains(search));

        var orders = await query
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.OrderDate,
                CustomerName = o.Customer != null ? o.Customer.Name : null,
                o.CreatedByUserId,
                o.Status,
                o.Subtotal,
                o.DiscountAmount,
                o.TotalAmount
            })
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();

        // total - returned goods - (paid - refunded); the same formula as Finance, debts and the order screen.
        var balances = await OrderBalance.LoadManyAsync(_db, orders.Select(o => (o.Id, o.TotalAmount)).ToList());

        var cashierIds = orders.Select(o => o.CreatedByUserId).Distinct().ToList();
        var cashierNames = await _db.Users.AsNoTracking()
            .Where(u => cashierIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        var result = orders.Select(o =>
        {
            var balance = balances[o.Id];
            var totalPaid = balance.NetPaid;
            var outstanding = balance.Outstanding;
            var payStatus = o.Status == OrderStatus.Returned ? "Refunded"
                : totalPaid <= 0 ? "Unpaid" : outstanding <= 0 ? "Paid" : "PartiallyPaid";

            return new SalesReportRowResult(
                o.Id, o.OrderNumber, o.OrderDate, o.CustomerName,
                cashierNames.GetValueOrDefault(o.CreatedByUserId, "—"),
                o.Status.ToString(), o.Subtotal, o.DiscountAmount, o.TotalAmount,
                totalPaid, outstanding, payStatus);
        }).ToList();

        if (!string.IsNullOrWhiteSpace(paymentStatus))
            result = result.Where(r => r.PaymentStatus == paymentStatus).ToList();

        return result;
    }

    // ===================== INVENTORY REPORT =====================

    public async Task<(List<InventoryReportRowResult> Items, int TotalCount)> GetInventoryReportAsync(
        string? search, int? categoryId, int? brandId, string? stockStatus, int page, int pageSize)
    {
        var rows = await GetInventoryReportRowsAsync(search, categoryId, brandId, stockStatus);
        var total = rows.Count;
        var pageItems = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (pageItems, total);
    }

    public async Task<List<InventoryReportRowResult>> GetInventoryReportForExportAsync(
        string? search, int? categoryId, int? brandId, string? stockStatus)
    {
        return await GetInventoryReportRowsAsync(search, categoryId, brandId, stockStatus);
    }

    private async Task<List<InventoryReportRowResult>> GetInventoryReportRowsAsync(
        string? search, int? categoryId, int? brandId, string? stockStatus)
    {
        var query = _db.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.SKU.Contains(search));

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId);

        if (brandId.HasValue)
            query = query.Where(p => p.BrandId == brandId);

        var products = await query
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.SKU,
                p.MinimumStockLevel,
                p.PurchasePrice,
                p.SalePrice,
                CategoryName = p.Category.Name,
                BrandName = p.Brand.Name
            })
            .ToListAsync();

        var stockByProduct = await _inventoryService.GetCurrentStockBulkAsync(products.Select(p => p.Id));

        var result = products.Select(p =>
        {
            var stock = stockByProduct.GetValueOrDefault(p.Id, 0);
            var status = stock <= 0 ? "OutOfStock" : stock <= p.MinimumStockLevel ? "LowStock" : "Normal";

            return new InventoryReportRowResult(
                p.Id, p.Name, p.SKU, p.CategoryName, p.BrandName,
                stock, p.MinimumStockLevel, status, p.PurchasePrice, p.SalePrice,
                stock * p.PurchasePrice);
        }).ToList();

        if (!string.IsNullOrWhiteSpace(stockStatus))
            result = result.Where(r => r.StockStatus == stockStatus).ToList();

        return result;
    }

    // ===================== CUSTOMER DEBT REPORT =====================
    // NOTE: debt per customer is always computed via PaymentService.GetCustomerDebtAsync
    // (the same single source of truth used by the existing GET ~/api/debts endpoint),
    // never re-derived independently — this satisfies the "no duplicate business logic" rule.

    public async Task<(List<CustomerDebtReportRowResult> Items, int TotalCount)> GetCustomerDebtReportAsync(
        string? search, int page, int pageSize)
    {
        var rows = await GetCustomerDebtReportRowsAsync(search);
        var total = rows.Count;
        var pageItems = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (pageItems, total);
    }

    public async Task<List<CustomerDebtReportRowResult>> GetCustomerDebtReportForExportAsync(string? search)
    {
        return await GetCustomerDebtReportRowsAsync(search);
    }

    private async Task<List<CustomerDebtReportRowResult>> GetCustomerDebtReportRowsAsync(string? search)
    {
        var customerQuery = _db.Customers.AsNoTracking(); // inactive customers keep their debt (still owed)

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            customerQuery = customerQuery.Where(c => c.Name.ToLower().Contains(term) || c.Phone.Contains(term));
        }

        var customers = await customerQuery.ToListAsync();
        var result = new List<CustomerDebtReportRowResult>();

        foreach (var customer in customers)
        {
            var debt = await _paymentService.GetCustomerDebtAsync(customer.Id);
            if (debt <= 0) continue;

            var customerOrders = await _db.Orders.AsNoTracking()
                .Where(o => o.CustomerId == customer.Id && SalesFilters.Receivable.Contains(o.Status))
                .Select(o => new { o.Id, o.TotalAmount, o.OrderDate })
                .ToListAsync();
            var customerBalances = await OrderBalance.LoadManyAsync(_db, customerOrders.Select(o => (o.Id, o.TotalAmount)).ToList());

            var outstandingOrderCount = customerBalances.Values.Count(b => b.Outstanding > 0m);
            DateTime? lastPurchase = customerOrders.Count == 0 ? null : customerOrders.Max(o => o.OrderDate);

            result.Add(new CustomerDebtReportRowResult(
                customer.Id, customer.Name, customer.Phone, outstandingOrderCount, debt, lastPurchase));
        }

        return result.OrderByDescending(r => r.TotalDebt).ToList();
    }

    // ===================== RETURNS REPORT (TASK C3) =====================

    private async Task<List<ReturnReportRowResult>> GetReturnRowsAsync(DateTime from, DateTime to)
    {
        var returns = await _db.OrderReturns.AsNoTracking()
            .Where(r => r.ReturnedAt >= from && r.ReturnedAt <= to)
            .OrderByDescending(r => r.ReturnedAt).ThenByDescending(r => r.Id)
            .Select(r => new
            {
                r.Id, r.ReturnNumber, r.ReturnedAt, r.OrderId,
                OrderNumber = r.Order.OrderNumber,
                CustomerName = r.Order.Customer != null ? r.Order.Customer.Name : null,
                r.ReturnedByUserId, r.Reason, r.GoodsValue, r.RefundAmount, r.RefundMethod, r.CostReversed, r.WriteOffCost,
                Units = r.Items.Sum(i => i.Quantity),
                Restocked = r.Items.Sum(i => i.RestockedQuantity),
                WrittenOff = r.Items.Sum(i => i.WrittenOffQuantity)
            })
            .ToListAsync();

        var userIds = returns.Select(r => r.ReturnedByUserId).Distinct().ToList();
        var userNames = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        return returns.Select(r => new ReturnReportRowResult(
            r.Id, r.ReturnNumber, r.ReturnedAt, r.OrderId, r.OrderNumber, r.CustomerName,
            userNames.GetValueOrDefault(r.ReturnedByUserId, "—"), r.Reason,
            r.Units, r.Restocked, r.WrittenOff, r.GoodsValue, r.RefundAmount,
            r.RefundMethod?.ToString(), r.CostReversed, r.WriteOffCost)).ToList();
    }

    public async Task<(List<ReturnReportRowResult> Items, int TotalCount, ReturnsReportTotalsResult Totals)> GetReturnsReportAsync(
        DateTime from, DateTime to, int page, int pageSize)
    {
        var rows = await GetReturnRowsAsync(from, to);
        var totals = new ReturnsReportTotalsResult(
            rows.Count,
            rows.Sum(r => r.GoodsValue),
            rows.Sum(r => r.RefundAmount),
            rows.Any(r => r.CostReversed is null) ? null : rows.Sum(r => r.CostReversed!.Value),
            rows.Any(r => r.WriteOffCost is null) ? null : rows.Sum(r => r.WriteOffCost!.Value));
        return (rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(), rows.Count, totals);
    }

    public Task<List<ReturnReportRowResult>> GetReturnsReportForExportAsync(DateTime from, DateTime to) =>
        GetReturnRowsAsync(from, to);

    // ===================== PURCHASE REPORT =====================

    public async Task<(List<PurchaseReportRowResult> Items, int TotalCount)> GetPurchaseReportAsync(
        DateTime from, DateTime to, int? supplierId, PurchaseStatus? status, string? search,
        int page, int pageSize)
    {
        var rows = await GetPurchaseReportRowsAsync(from, to, supplierId, status, search);
        var total = rows.Count;
        var pageItems = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (pageItems, total);
    }

    public async Task<List<PurchaseReportRowResult>> GetPurchaseReportForExportAsync(
        DateTime from, DateTime to, int? supplierId, PurchaseStatus? status, string? search)
    {
        return await GetPurchaseReportRowsAsync(from, to, supplierId, status, search);
    }

    private async Task<List<PurchaseReportRowResult>> GetPurchaseReportRowsAsync(
        DateTime from, DateTime to, int? supplierId, PurchaseStatus? status, string? search)
    {
        var query = _db.Purchases.AsNoTracking()
            .Where(p => p.PurchaseDate >= from && p.PurchaseDate <= to);

        if (supplierId.HasValue)
            query = query.Where(p => p.SupplierId == supplierId);

        if (status.HasValue)
            query = query.Where(p => p.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.PurchaseNumber.Contains(search));

        var purchases = await query
            .Select(p => new
            {
                p.Id,
                p.PurchaseNumber,
                p.PurchaseDate,
                SupplierName = p.Supplier.Name,
                p.Status,
                p.Subtotal,
                p.DiscountAmount,
                p.TotalAmount,
                p.CreatedByUserId,
                p.ConfirmedAt
            })
            .OrderByDescending(p => p.PurchaseDate)
            .ToListAsync();

        var userIds = purchases.Select(p => p.CreatedByUserId).Distinct().ToList();
        var userNames = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        return purchases.Select(p => new PurchaseReportRowResult(
            p.Id, p.PurchaseNumber, p.PurchaseDate, p.SupplierName, p.Status.ToString(),
            p.Subtotal, p.DiscountAmount, p.TotalAmount,
            userNames.GetValueOrDefault(p.CreatedByUserId, "—"), p.ConfirmedAt)).ToList();
    }

    // ===================== EXPENSE REPORT =====================

    public async Task<(List<ExpenseReportRowResult> Items, int TotalCount)> GetExpenseReportAsync(
        DateTime from, DateTime to, int? categoryId, bool? isVoided, string? search,
        int page, int pageSize)
    {
        var rows = await GetExpenseReportRowsAsync(from, to, categoryId, isVoided, search);
        var total = rows.Count;
        var pageItems = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (pageItems, total);
    }

    public async Task<List<ExpenseReportRowResult>> GetExpenseReportForExportAsync(
        DateTime from, DateTime to, int? categoryId, bool? isVoided, string? search)
    {
        return await GetExpenseReportRowsAsync(from, to, categoryId, isVoided, search);
    }

    private async Task<List<ExpenseReportRowResult>> GetExpenseReportRowsAsync(
        DateTime from, DateTime to, int? categoryId, bool? isVoided, string? search)
    {
        var query = _db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= from && e.ExpenseDate <= to);

        if (categoryId.HasValue)
            query = query.Where(e => e.ExpenseCategoryId == categoryId);

        if (isVoided.HasValue)
            query = query.Where(e => e.IsVoided == isVoided);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e => e.ExpenseNumber.Contains(search) || e.Description.Contains(search));

        var expenses = await query
            .Select(e => new
            {
                e.Id,
                e.ExpenseNumber,
                e.ExpenseDate,
                CategoryName = e.ExpenseCategory.Name,
                e.Description,
                e.Amount,
                e.CreatedByUserId,
                e.IsVoided
            })
            .OrderByDescending(e => e.ExpenseDate)
            .ToListAsync();

        var userIds = expenses.Select(e => e.CreatedByUserId).Distinct().ToList();
        var userNames = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        return expenses.Select(e => new ExpenseReportRowResult(
            e.Id, e.ExpenseNumber, e.ExpenseDate, e.CategoryName, e.Description, e.Amount,
            userNames.GetValueOrDefault(e.CreatedByUserId, "—"), e.IsVoided)).ToList();
    }

    // ===================== PAYMENT REPORT =====================

    public async Task<(List<PaymentReportRowResult> Items, int TotalCount)> GetPaymentReportAsync(
        DateTime from, DateTime to, int? customerId, PaymentMethod? method, PaymentStatus? status,
        string? search, int page, int pageSize)
    {
        var rows = await GetPaymentReportRowsAsync(from, to, customerId, method, status, search);
        var total = rows.Count;
        var pageItems = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (pageItems, total);
    }

    public async Task<List<PaymentReportRowResult>> GetPaymentReportForExportAsync(
        DateTime from, DateTime to, int? customerId, PaymentMethod? method, PaymentStatus? status, string? search)
    {
        return await GetPaymentReportRowsAsync(from, to, customerId, method, status, search);
    }

    private async Task<List<PaymentReportRowResult>> GetPaymentReportRowsAsync(
        DateTime from, DateTime to, int? customerId, PaymentMethod? method, PaymentStatus? status, string? search)
    {
        var query = _db.Payments.AsNoTracking()
            .Include(p => p.Order)
            .Include(p => p.Customer)
            .Where(p => p.CreatedAt >= from && p.CreatedAt <= to);

        if (customerId.HasValue)
            query = query.Where(p => p.CustomerId == customerId);

        if (method.HasValue)
            query = query.Where(p => p.Method == method);

        if (status.HasValue)
            query = query.Where(p => p.Status == status);

        var payments = await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            payments = payments.Where(p =>
                p.PaymentNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Order.OrderNumber.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var userIds = payments.Select(p => p.ReceivedByUserId).Distinct().ToList();
        var userNames = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        return payments.Select(p => new PaymentReportRowResult(
            p.Id, p.PaymentNumber, p.CreatedAt, p.Order.OrderNumber,
            p.Customer != null ? p.Customer.Name : "Walk-in Customer",
            p.Amount, p.Method.ToString(), p.Status.ToString(),
            userNames.GetValueOrDefault(p.ReceivedByUserId, "—"))).ToList();
    }
}