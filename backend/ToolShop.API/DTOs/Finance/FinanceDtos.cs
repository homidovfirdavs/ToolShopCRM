namespace ToolShop.API.DTOs.Finance;

public record FinanceSummaryResponse(
    decimal Revenue,
    decimal Cogs,
    decimal GrossProfit,
    decimal OperatingExpenses,
    decimal NetOperatingProfit,
    decimal PaymentsReceived,
    decimal AccountsReceivable,
    decimal PurchaseSpend,
    int CompletedOrdersCount,
    decimal AverageOrderValue,
    decimal GrossMarginPercent,
    decimal NetOperatingMarginPercent,
    int UnknownCostItemsCount,
    decimal ReturnsValue,
    decimal RefundsPaid,
    decimal WriteOffCost);

public record DailyFinanceItem(
    DateOnly Date, decimal Revenue, decimal Cogs, decimal GrossProfit,
    decimal Expenses, decimal NetOperatingProfit, decimal PaymentsReceived);

public record MonthlyFinanceItem(
    int Year, int Month, decimal Revenue, decimal Cogs, decimal GrossProfit,
    decimal Expenses, decimal NetOperatingProfit, decimal PaymentsReceived, decimal PurchaseSpend);

public record ExpenseCategoryBreakdownItem(string Category, decimal Amount, decimal Percentage);

public record PaymentMethodBreakdownItem(string Method, decimal Amount, int Count);

public record ProfitByProductItem(
    int ProductId, string ProductName, string Sku, int QuantitySold,
    decimal Revenue, decimal Cogs, decimal GrossProfit, decimal GrossMarginPercent);

public record ProfitByCategoryItem(
    string Category, int QuantitySold, decimal Revenue, decimal Cogs,
    decimal GrossProfit, decimal MarginPercent);