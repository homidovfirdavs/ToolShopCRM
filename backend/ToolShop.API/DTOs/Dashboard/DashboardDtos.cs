namespace ToolShop.API.DTOs.Dashboard;

public record DashboardSummaryResponse(
    DateOnly DateFrom, DateOnly DateTo,
    decimal SalesRevenue, decimal? GrossProfit, decimal PaymentsReceived,
    decimal AccountsReceivable, int LowStockCount, decimal OperatingExpenses,
    bool HasCompleteCostData, int OutOfStockCount, int CompletedOrdersCount);

public record SalesTrendPointResponse(DateOnly Date, decimal Revenue, decimal GrossProfit, bool HasCompleteCostData);

public record CategoryBreakdownResponse(string Category, decimal Revenue, decimal Percentage);

public record AttentionItemResponse(string Type, string Label, int Count, decimal? Amount, string Link);

public record LowStockItemResponse(
    int ProductId, string ProductName, string Sku, int CurrentStock, int MinimumStockLevel, string Status);

public record FinancialSummaryShortResponse(
    decimal Revenue, decimal? Cogs, decimal? GrossProfit, decimal OperatingExpenses,
    decimal? NetOperatingProfit, bool HasCompleteCostData);