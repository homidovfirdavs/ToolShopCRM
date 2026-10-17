using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Infrastructure.Finance;
using ToolShop.Infrastructure.Inventory;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Dashboard;

public record DashboardSummaryResult(
    DateOnly DateFrom, DateOnly DateTo,
    decimal SalesRevenue, decimal? GrossProfit, decimal PaymentsReceived,
    decimal AccountsReceivable, int LowStockCount, decimal OperatingExpenses,
    bool HasCompleteCostData, int OutOfStockCount, int CompletedOrdersCount);

public record SalesTrendPointResult(DateOnly Date, decimal Revenue, decimal GrossProfit, bool HasCompleteCostData);

public record CategoryBreakdownResult(string Category, decimal Revenue, decimal Percentage);

public record AttentionItemResult(string Type, string Label, int Count, decimal? Amount, string Link);

public record LowStockItemResult(
    int ProductId, string ProductName, string Sku, int CurrentStock, int MinimumStockLevel, string Status);

public record FinancialSummaryShortResult(
    decimal Revenue, decimal? Cogs, decimal? GrossProfit, decimal OperatingExpenses,
    decimal? NetOperatingProfit, bool HasCompleteCostData);

public record TopProductItemResult(
    int ProductId, string ProductName, string Sku, int QuantitySold, decimal Revenue, decimal? GrossProfit);

public record TopProductsResult(
    DateOnly DateFrom, DateOnly DateTo,
    List<TopProductItemResult> BestSelling,
    List<TopProductItemResult> MostProfitable,
    List<TopProductItemResult> UnknownProfit);

public class DashboardService
{
    private readonly AppDbContext _db;
    private readonly FinanceService _financeService;
    private readonly InventoryService _inventoryService;

    public DashboardService(AppDbContext db, FinanceService financeService, InventoryService inventoryService)
    {
        _db = db;
        _financeService = financeService;
        _inventoryService = inventoryService;
    }

    public async Task<DashboardSummaryResult> GetSummaryAsync(DateTime from, DateTime to)
    {
        var finance = await _financeService.GetSummaryAsync(from, to);
        var (lowStockCount, outOfStockCount) = await GetStockCountsAsync();
        var hasCompleteCostData = finance.UnknownCostItemsCount == 0;

        return new DashboardSummaryResult(
            DateOnly.FromDateTime(from), DateOnly.FromDateTime(to),
            finance.Revenue,
            hasCompleteCostData ? finance.GrossProfit : null,
            finance.PaymentsReceived,
            finance.AccountsReceivable,
            lowStockCount,
            finance.OperatingExpenses,
            hasCompleteCostData,
            outOfStockCount,
            finance.CompletedOrdersCount);
    }

    public async Task<List<SalesTrendPointResult>> GetSalesTrendAsync(DateTime from, DateTime to)
    {
        var daily = await _financeService.GetDailyAsync(from, to);
        return daily
            .Select(d => new SalesTrendPointResult(d.Date, d.Revenue, d.GrossProfit, d.HasCompleteCostData))
            .ToList();
    }

    public async Task<List<CategoryBreakdownResult>> GetSalesByCategoryAsync(DateTime from, DateTime to)
    {
        var byCategory = await _financeService.GetProfitByCategoryAsync(from, to);
        var totalRevenue = byCategory.Sum(c => c.Revenue);

        return byCategory
            .OrderByDescending(c => c.Revenue)
            .Select(c => new CategoryBreakdownResult(
                c.Category, c.Revenue, totalRevenue == 0 ? 0m : Math.Round(c.Revenue / totalRevenue * 100, 2)))
            .ToList();
    }

    public async Task<List<AttentionItemResult>> GetAttentionAsync(DateTime from, DateTime to)
    {
        var (lowStockCount, outOfStockCount) = await GetStockCountsAsync();
        var finance = await _financeService.GetSummaryAsync(from, to);

        var draftPurchasesCount = await _db.Purchases
            .AsNoTracking()
            .CountAsync(p => p.Status == PurchaseStatus.Draft);

        return
        [
            new AttentionItemResult("OutOfStock", "Дар анбор нест", outOfStockCount, null, "/inventory?status=OutOfStock"),
            new AttentionItemResult("LowStock", "Захираи кам", lowStockCount, null, "/inventory?status=LowStock"),
            new AttentionItemResult("OutstandingDebt", "Қарзи мизоҷон", 0, finance.AccountsReceivable, "/debts"),
            new AttentionItemResult("DraftPurchases", "Харидҳои тасдиқнашуда", draftPurchasesCount, null, "/purchases?status=Draft")
        ];
    }

    public async Task<List<LowStockItemResult>> GetLowStockProductsAsync(int top = 10)
    {
        var products = await _db.Products.AsNoTracking().Where(p => p.IsActive)
            .Select(p => new { p.Id, p.Name, p.SKU, p.MinimumStockLevel })
            .ToListAsync();

        var stockByProduct = await _inventoryService.GetCurrentStockBulkAsync(products.Select(p => p.Id));

        return products
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.SKU,
                p.MinimumStockLevel,
                CurrentStock = stockByProduct.GetValueOrDefault(p.Id, 0)
            })
            .Where(x => x.CurrentStock <= x.MinimumStockLevel)
            .OrderBy(x => x.CurrentStock)
            .Take(top)
            .Select(x => new LowStockItemResult(
                x.Id, x.Name, x.SKU, x.CurrentStock, x.MinimumStockLevel,
                x.CurrentStock <= 0 ? "OutOfStock" : "LowStock"))
            .ToList();
    }

    /// <summary>
    /// Top-7. Uses the SAME per-product numbers as the Product Performance report (net of returns, CostAtSale based, report days in
    /// shop-local time) - no formula of its own.
    ///   BestSelling    - by net quantity sold (sales minus returns).
    ///   MostProfitable - by product gross profit; ONLY products whose cost is fully known and whose profit is positive.
    ///   UnknownProfit  - sold products whose profit is N/A (some sold unit has no known cost). They never enter the profit ranking
    ///                    and are never shown as 0.
    /// </summary>
    public async Task<TopProductsResult> GetTopProductsAsync(DateTime from, DateTime to, int top = 5)
    {
        top = Math.Clamp(top, 1, 20);
        var (all, _) = await _financeService.GetProfitByProductAsync(from, to, "revenue", 1, int.MaxValue);

        static TopProductItemResult Map(ProfitByProductResult p) =>
            new(p.ProductId, p.ProductName, p.Sku, p.QuantitySold, p.Revenue, p.GrossProfit);

        var bestSelling = all
            .Where(p => p.QuantitySold > 0)
            .OrderByDescending(p => p.QuantitySold).ThenByDescending(p => p.Revenue).ThenBy(p => p.ProductName)
            .Take(top).Select(Map).ToList();

        var mostProfitable = all
            .Where(p => p.HasCompleteCostData && p.GrossProfit is > 0m)
            .OrderByDescending(p => p.GrossProfit).ThenBy(p => p.ProductName)
            .Take(top).Select(Map).ToList();

        var unknownProfit = all
            .Where(p => !p.HasCompleteCostData && p.QuantitySold > 0)
            .OrderByDescending(p => p.Revenue).ThenBy(p => p.ProductName)
            .Take(top).Select(Map).ToList();

        return new TopProductsResult(
            DateOnly.FromDateTime(from), DateOnly.FromDateTime(to), bestSelling, mostProfitable, unknownProfit);
    }

    public async Task<FinancialSummaryShortResult> GetFinancialSummaryShortAsync(DateTime from, DateTime to)
    {
        var finance = await _financeService.GetSummaryAsync(from, to);
        var hasCompleteCostData = finance.UnknownCostItemsCount == 0;

        return new FinancialSummaryShortResult(
            finance.Revenue,
            hasCompleteCostData ? finance.Cogs : null,
            hasCompleteCostData ? finance.GrossProfit : null,
            finance.OperatingExpenses,
            hasCompleteCostData ? finance.NetOperatingProfit : null,
            hasCompleteCostData);
    }

    private async Task<(int LowStockCount, int OutOfStockCount)> GetStockCountsAsync()
    {
        var products = await _db.Products.AsNoTracking().Where(p => p.IsActive)
            .Select(p => new { p.Id, p.MinimumStockLevel })
            .ToListAsync();

        var stockByProduct = await _inventoryService.GetCurrentStockBulkAsync(products.Select(p => p.Id));

        var lowStockCount = products.Count(p =>
        {
            var stock = stockByProduct.GetValueOrDefault(p.Id, 0);
            return stock > 0 && stock <= p.MinimumStockLevel;
        });

        var outOfStockCount = products.Count(p => stockByProduct.GetValueOrDefault(p.Id, 0) <= 0);

        return (lowStockCount, outOfStockCount);
    }
}