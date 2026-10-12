namespace ToolShop.API.DTOs.Reports;

public record SalesReportRowResponse(
    int OrderId, string OrderNumber, DateTime OrderDate, string? CustomerName,
    string CashierName, string Status, decimal Subtotal, decimal DiscountAmount,
    decimal TotalAmount, decimal TotalPaid, decimal OutstandingBalance, string PaymentStatus);

public record InventoryReportRowResponse(
    int ProductId, string ProductName, string Sku, string CategoryName, string BrandName,
    int CurrentStock, int MinimumStockLevel, string StockStatus, decimal PurchasePrice,
    decimal SalePrice, decimal StockValue);

public record CustomerDebtReportRowResponse(
    int CustomerId, string CustomerName, string Phone, int OutstandingOrderCount,
    decimal TotalDebt, DateTime? LastPurchaseDate);

public record PurchaseReportRowResponse(
    int PurchaseId, string PurchaseNumber, DateTime PurchaseDate, string SupplierName,
    string Status, decimal Subtotal, decimal DiscountAmount, decimal TotalAmount,
    string CreatedByUserName, DateTime? ConfirmedAt);

public record ExpenseReportRowResponse(
    int ExpenseId, string ExpenseNumber, DateTime ExpenseDate, string CategoryName,
    string Description, decimal Amount, string CreatedByUserName, bool IsVoided);

public record PaymentReportRowResponse(
    int PaymentId, string PaymentNumber, DateTime CreatedAt, string OrderNumber,
    string CustomerName, decimal Amount, string Method, string Status, string ReceivedByUserName);

public record ProductPerformanceRowResponse(
    int ProductId, string ProductName, string Sku, int QuantitySold,
    decimal Revenue, decimal? Cogs, decimal? GrossProfit, decimal? GrossMarginPercent,
    bool HasCompleteCostData);

public record CategoryPerformanceRowResponse(
    string Category, int QuantitySold, decimal Revenue, decimal? Cogs,
    decimal? GrossProfit, decimal? MarginPercent, bool HasCompleteCostData);

public record ProfitabilityReportResponse(
    DateOnly DateFrom, DateOnly DateTo,
    decimal Revenue, decimal? Cogs, decimal? GrossProfit, decimal? GrossMarginPercent,
    decimal OperatingExpenses, decimal? NetOperatingProfit, decimal? NetOperatingMarginPercent,
    decimal PaymentsReceived, decimal AccountsReceivable, decimal PurchaseSpend,
    int CompletedOrdersCount, decimal AverageOrderValue, bool HasCompleteCostData);