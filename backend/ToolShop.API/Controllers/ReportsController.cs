using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToolShop.API.DTOs.Catalog;
using ToolShop.API.DTOs.Reports;
using ToolShop.API.Helpers;
using ToolShop.Domain.Authorization;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Reports;
using ToolShop.Infrastructure.Finance;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = Permissions.ReportsView)]
public class ReportsController : ControllerBase
{
       private readonly ReportsService _reportsService;
    private readonly FinanceService _financeService;

    public ReportsController(ReportsService reportsService, FinanceService financeService)
    {
        _reportsService = reportsService;
        _financeService = financeService;
    }

   private static (DateTime From, DateTime To) ResolveRange(DateTime? from, DateTime? to)
{
    var toDate = to.HasValue ? DateTime.SpecifyKind(to.Value, DateTimeKind.Utc).Date : DateTime.UtcNow.Date;
    var fromDate = from.HasValue ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc).Date : toDate.AddDays(-29);

    var effectiveFrom = fromDate;
    var effectiveTo = toDate.AddDays(1).AddTicks(-1); // то охири рӯзи "to", фарогир (inclusive)

    return (effectiveFrom, effectiveTo);
}

    // ===================== SALES REPORT =====================

    [HttpGet("sales")]
    public async Task<ActionResult<PagedResult<SalesReportRowResponse>>> GetSalesReport(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? customerId, [FromQuery] OrderStatus? status,
        [FromQuery] string? cashierUserId, [FromQuery] string? paymentStatus,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (f, t) = ResolveRange(from, to);
        var (items, total) = await _reportsService.GetSalesReportAsync(
            f, t, customerId, status, cashierUserId, paymentStatus, search, page, pageSize);

        return Ok(new PagedResult<SalesReportRowResponse>
        {
            Items = items.Select(MapSalesRow).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    /// <summary>
    /// Totals across ALL rows matching the filters (not just the current page).
    /// Additive endpoint: existing sales report endpoints are unchanged.
    /// </summary>
    [HttpGet("sales/summary")]
    public async Task<ActionResult<SalesReportSummaryResponse>> GetSalesReportSummary(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? customerId, [FromQuery] OrderStatus? status,
        [FromQuery] string? cashierUserId, [FromQuery] string? paymentStatus,
        [FromQuery] string? search)
    {
        var (f, t) = ResolveRange(from, to);
        var rows = await _reportsService.GetSalesReportForExportAsync(
            f, t, customerId, status, cashierUserId, paymentStatus, search);

        var completed = rows.Where(r => r.Status == nameof(OrderStatus.Completed)
                                     || r.Status == nameof(OrderStatus.PartiallyReturned)).ToList();

        return Ok(new SalesReportSummaryResponse(
            rows.Count,
            rows.Sum(r => r.Subtotal),
            rows.Sum(r => r.DiscountAmount),
            rows.Sum(r => r.TotalAmount),
            rows.Sum(r => r.TotalPaid),
            rows.Sum(r => r.OutstandingBalance),
            completed.Count,
            completed.Sum(r => r.TotalAmount),
            completed.Sum(r => r.TotalPaid),
            completed.Sum(r => r.OutstandingBalance)));
    }

    [HttpGet("sales/export")]
    public async Task<IActionResult> ExportSalesReport(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? customerId, [FromQuery] OrderStatus? status,
        [FromQuery] string? cashierUserId, [FromQuery] string? paymentStatus,
        [FromQuery] string? search)
    {
        var (f, t) = ResolveRange(from, to);
        var rows = await _reportsService.GetSalesReportForExportAsync(
            f, t, customerId, status, cashierUserId, paymentStatus, search);

        var headers = new[]
        {
            "Order Number", "Order Date", "Customer", "Cashier", "Status",
            "Subtotal", "Discount", "Total Amount", "Total Paid", "Outstanding", "Payment Status"
        };

        var csvRows = rows.Select(r => new[]
        {
            r.OrderNumber,
            r.OrderDate.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            r.CustomerName ?? "",
            r.CashierName,
            r.Status,
            r.Subtotal.ToString("F2", CultureInfo.InvariantCulture),
            r.DiscountAmount.ToString("F2", CultureInfo.InvariantCulture),
            r.TotalAmount.ToString("F2", CultureInfo.InvariantCulture),
            r.TotalPaid.ToString("F2", CultureInfo.InvariantCulture),
            r.OutstandingBalance.ToString("F2", CultureInfo.InvariantCulture),
            r.PaymentStatus
        });

        var csvBytes = CsvExportHelper.BuildCsv(headers, csvRows);
        return File(csvBytes, "text/csv", $"sales-report-{f:yyyyMMdd}-{t:yyyyMMdd}.csv");
    }

    // ===================== RETURNS REPORT (by date of the RETURN) =====================

    [HttpGet("returns")]
    public async Task<ActionResult<ReturnsReportResponse>> GetReturnsReport(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var (f, t) = ResolveRange(from, to);
        var (items, total, totals) = await _reportsService.GetReturnsReportAsync(f, t, page, pageSize);

        return Ok(new ReturnsReportResponse(
            items.Select(MapReturnRow).ToList(), page, pageSize, total,
            (int)Math.Ceiling(total / (double)pageSize),
            totals.ReturnsCount, totals.GoodsValue, totals.RefundsPaid, totals.CostReversed, totals.WriteOffCost));
    }

    private static ReturnReportRowResponse MapReturnRow(ReturnReportRowResult r) => new(
        r.ReturnId, r.ReturnNumber, r.ReturnedAt, r.OrderId, r.OrderNumber, r.CustomerName, r.ReturnedByUserName,
        r.Reason, r.ReturnedUnits, r.RestockedUnits, r.WrittenOffUnits, r.GoodsValue, r.RefundAmount,
        r.RefundMethod, r.CostReversed, r.WriteOffCost);

    // ===================== INVENTORY REPORT =====================

    [HttpGet("inventory")]
    public async Task<ActionResult<PagedResult<InventoryReportRowResponse>>> GetInventoryReport(
        [FromQuery] string? search, [FromQuery] int? categoryId, [FromQuery] int? brandId,
        [FromQuery] string? stockStatus, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (items, total) = await _reportsService.GetInventoryReportAsync(
            search, categoryId, brandId, stockStatus, page, pageSize);

        return Ok(new PagedResult<InventoryReportRowResponse>
        {
            Items = items.Select(MapInventoryRow).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("inventory/export")]
    public async Task<IActionResult> ExportInventoryReport(
        [FromQuery] string? search, [FromQuery] int? categoryId, [FromQuery] int? brandId,
        [FromQuery] string? stockStatus)
    {
        var rows = await _reportsService.GetInventoryReportForExportAsync(search, categoryId, brandId, stockStatus);

        var headers = new[]
        {
            "Product", "SKU", "Category", "Brand", "Current Stock", "Minimum Stock",
            "Status", "Purchase Price", "Sale Price", "Stock Value"
        };

        var csvRows = rows.Select(r => new[]
        {
            r.ProductName, r.Sku, r.CategoryName, r.BrandName,
            r.CurrentStock.ToString(CultureInfo.InvariantCulture),
            r.MinimumStockLevel.ToString(CultureInfo.InvariantCulture),
            r.StockStatus,
            r.PurchasePrice.ToString("F2", CultureInfo.InvariantCulture),
            r.SalePrice.ToString("F2", CultureInfo.InvariantCulture),
            r.StockValue.ToString("F2", CultureInfo.InvariantCulture)
        });

        var csvBytes = CsvExportHelper.BuildCsv(headers, csvRows);
        return File(csvBytes, "text/csv", $"inventory-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ===================== CUSTOMER DEBT REPORT =====================

    [HttpGet("customer-debt")]
    public async Task<ActionResult<PagedResult<CustomerDebtReportRowResponse>>> GetCustomerDebtReport(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (items, total) = await _reportsService.GetCustomerDebtReportAsync(search, page, pageSize);

        return Ok(new PagedResult<CustomerDebtReportRowResponse>
        {
            Items = items.Select(MapCustomerDebtRow).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("customer-debt/export")]
    public async Task<IActionResult> ExportCustomerDebtReport([FromQuery] string? search)
    {
        var rows = await _reportsService.GetCustomerDebtReportForExportAsync(search);

        var headers = new[] { "Customer", "Phone", "Outstanding Orders", "Total Debt", "Last Purchase" };

        var csvRows = rows.Select(r => new[]
        {
            r.CustomerName, r.Phone, r.OutstandingOrderCount.ToString(CultureInfo.InvariantCulture),
            r.TotalDebt.ToString("F2", CultureInfo.InvariantCulture),
            r.LastPurchaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""
        });

        var csvBytes = CsvExportHelper.BuildCsv(headers, csvRows);
        return File(csvBytes, "text/csv", $"customer-debt-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ===================== PURCHASE REPORT =====================

    [HttpGet("purchases")]
    public async Task<ActionResult<PagedResult<PurchaseReportRowResponse>>> GetPurchaseReport(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? supplierId, [FromQuery] PurchaseStatus? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (f, t) = ResolveRange(from, to);
        var (items, total) = await _reportsService.GetPurchaseReportAsync(f, t, supplierId, status, search, page, pageSize);

        return Ok(new PagedResult<PurchaseReportRowResponse>
        {
            Items = items.Select(MapPurchaseRow).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("purchases/export")]
    public async Task<IActionResult> ExportPurchaseReport(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? supplierId, [FromQuery] PurchaseStatus? status, [FromQuery] string? search)
    {
        var (f, t) = ResolveRange(from, to);
        var rows = await _reportsService.GetPurchaseReportForExportAsync(f, t, supplierId, status, search);

        var headers = new[]
        {
            "Purchase Number", "Purchase Date", "Supplier", "Status",
            "Subtotal", "Discount", "Total Amount", "Created By", "Confirmed At"
        };

        var csvRows = rows.Select(r => new[]
        {
            r.PurchaseNumber,
            r.PurchaseDate.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            r.SupplierName, r.Status,
            r.Subtotal.ToString("F2", CultureInfo.InvariantCulture),
            r.DiscountAmount.ToString("F2", CultureInfo.InvariantCulture),
            r.TotalAmount.ToString("F2", CultureInfo.InvariantCulture),
            r.CreatedByUserName,
            r.ConfirmedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? ""
        });

        var csvBytes = CsvExportHelper.BuildCsv(headers, csvRows);
        return File(csvBytes, "text/csv", $"purchase-report-{f:yyyyMMdd}-{t:yyyyMMdd}.csv");
    }

    // ===================== EXPENSE REPORT =====================

    [HttpGet("expenses")]
    public async Task<ActionResult<PagedResult<ExpenseReportRowResponse>>> GetExpenseReport(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? categoryId, [FromQuery] bool? isVoided, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (f, t) = ResolveRange(from, to);
        var (items, total) = await _reportsService.GetExpenseReportAsync(f, t, categoryId, isVoided, search, page, pageSize);

        return Ok(new PagedResult<ExpenseReportRowResponse>
        {
            Items = items.Select(MapExpenseRow).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("expenses/export")]
    public async Task<IActionResult> ExportExpenseReport(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? categoryId, [FromQuery] bool? isVoided, [FromQuery] string? search)
    {
        var (f, t) = ResolveRange(from, to);
        var rows = await _reportsService.GetExpenseReportForExportAsync(f, t, categoryId, isVoided, search);

        var headers = new[]
        {
            "Expense Number", "Expense Date", "Category", "Description", "Amount", "Created By", "Voided"
        };

        var csvRows = rows.Select(r => new[]
        {
            r.ExpenseNumber,
            r.ExpenseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            r.CategoryName, r.Description,
            r.Amount.ToString("F2", CultureInfo.InvariantCulture),
            r.CreatedByUserName,
            r.IsVoided ? "Yes" : "No"
        });

        var csvBytes = CsvExportHelper.BuildCsv(headers, csvRows);
        return File(csvBytes, "text/csv", $"expense-report-{f:yyyyMMdd}-{t:yyyyMMdd}.csv");
    }

    // ===================== PAYMENT REPORT =====================

    [HttpGet("payments")]
    public async Task<ActionResult<PagedResult<PaymentReportRowResponse>>> GetPaymentReport(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? customerId, [FromQuery] PaymentMethod? method, [FromQuery] PaymentStatus? status,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (f, t) = ResolveRange(from, to);
        var (items, total) = await _reportsService.GetPaymentReportAsync(f, t, customerId, method, status, search, page, pageSize);

        return Ok(new PagedResult<PaymentReportRowResponse>
        {
            Items = items.Select(MapPaymentRow).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("payments/export")]
    public async Task<IActionResult> ExportPaymentReport(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? customerId, [FromQuery] PaymentMethod? method, [FromQuery] PaymentStatus? status,
        [FromQuery] string? search)
    {
        var (f, t) = ResolveRange(from, to);
        var rows = await _reportsService.GetPaymentReportForExportAsync(f, t, customerId, method, status, search);

        var headers = new[]
        {
            "Payment Number", "Date", "Order Number", "Customer", "Amount", "Method", "Status", "Received By"
        };

        var csvRows = rows.Select(r => new[]
        {
            r.PaymentNumber,
            r.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            r.OrderNumber, r.CustomerName,
            r.Amount.ToString("F2", CultureInfo.InvariantCulture),
            r.Method, r.Status, r.ReceivedByUserName
        });

        var csvBytes = CsvExportHelper.BuildCsv(headers, csvRows);
        return File(csvBytes, "text/csv", $"payment-report-{f:yyyyMMdd}-{t:yyyyMMdd}.csv");
    }
    // ===================== PRODUCT PERFORMANCE REPORT =====================

    [HttpGet("product-performance")]
    public async Task<ActionResult<PagedResult<ProductPerformanceRowResponse>>> GetProductPerformance(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string sortBy = "revenue", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (f, t) = ResolveRange(from, to);
        var (items, total) = await _financeService.GetProfitByProductAsync(f, t, sortBy, page, pageSize);

                return Ok(new PagedResult<ProductPerformanceRowResponse>
        {
            Items = items.Select(r => new ProductPerformanceRowResponse(
                r.ProductId, r.ProductName, r.Sku, r.QuantitySold, r.Revenue, r.Cogs, r.GrossProfit, r.GrossMarginPercent, r.HasCompleteCostData)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    // ===================== CATEGORY PERFORMANCE REPORT =====================

    [HttpGet("category-performance")]
    public async Task<ActionResult<List<CategoryPerformanceRowResponse>>> GetCategoryPerformance(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
                var items = await _financeService.GetProfitByCategoryAsync(f, t);
        return Ok(items.Select(r => new CategoryPerformanceRowResponse(
            r.Category, r.QuantitySold, r.Revenue, r.Cogs, r.GrossProfit, r.MarginPercent, r.HasCompleteCostData)).ToList());
    }

    // ===================== PROFITABILITY REPORT =====================

    [HttpGet("profitability")]
    public async Task<ActionResult<ProfitabilityReportResponse>> GetProfitability(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var finance = await _financeService.GetSummaryAsync(f, t);
        var hasCompleteCostData = finance.UnknownCostItemsCount == 0;

        return Ok(new ProfitabilityReportResponse(
            DateOnly.FromDateTime(f), DateOnly.FromDateTime(t),
            finance.Revenue,
            hasCompleteCostData ? finance.Cogs : null,
            hasCompleteCostData ? finance.GrossProfit : null,
            hasCompleteCostData ? finance.GrossMarginPercent : null,
            finance.OperatingExpenses,
            hasCompleteCostData ? finance.NetOperatingProfit : null,
            hasCompleteCostData ? finance.NetOperatingMarginPercent : null,
            finance.PaymentsReceived, finance.AccountsReceivable, finance.PurchaseSpend,
            finance.CompletedOrdersCount, finance.AverageOrderValue, hasCompleteCostData));
    }

    // ===================== MAPPERS =====================

    private static SalesReportRowResponse MapSalesRow(SalesReportRowResult r) => new(
        r.OrderId, r.OrderNumber, r.OrderDate, r.CustomerName, r.CashierName, r.Status,
        r.Subtotal, r.DiscountAmount, r.TotalAmount, r.TotalPaid, r.OutstandingBalance, r.PaymentStatus);

    private static InventoryReportRowResponse MapInventoryRow(InventoryReportRowResult r) => new(
        r.ProductId, r.ProductName, r.Sku, r.CategoryName, r.BrandName, r.CurrentStock,
        r.MinimumStockLevel, r.StockStatus, r.PurchasePrice, r.SalePrice, r.StockValue);

    private static CustomerDebtReportRowResponse MapCustomerDebtRow(CustomerDebtReportRowResult r) => new(
        r.CustomerId, r.CustomerName, r.Phone, r.OutstandingOrderCount, r.TotalDebt, r.LastPurchaseDate);

    private static PurchaseReportRowResponse MapPurchaseRow(PurchaseReportRowResult r) => new(
        r.PurchaseId, r.PurchaseNumber, r.PurchaseDate, r.SupplierName, r.Status,
        r.Subtotal, r.DiscountAmount, r.TotalAmount, r.CreatedByUserName, r.ConfirmedAt);

    private static ExpenseReportRowResponse MapExpenseRow(ExpenseReportRowResult r) => new(
        r.ExpenseId, r.ExpenseNumber, r.ExpenseDate, r.CategoryName, r.Description,
        r.Amount, r.CreatedByUserName, r.IsVoided);

    private static PaymentReportRowResponse MapPaymentRow(PaymentReportRowResult r) => new(
        r.PaymentId, r.PaymentNumber, r.CreatedAt, r.OrderNumber, r.CustomerName,
        r.Amount, r.Method, r.Status, r.ReceivedByUserName);
}

public record SalesReportSummaryResponse(
    int OrderCount, decimal Subtotal, decimal DiscountAmount, decimal TotalAmount, decimal TotalPaid, decimal OutstandingBalance,
    int CompletedOrderCount, decimal CompletedTotalAmount, decimal CompletedTotalPaid, decimal CompletedOutstandingBalance);
