using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToolShop.API.DTOs.Finance;
using ToolShop.Domain.Authorization;
using ToolShop.Infrastructure.Finance;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/finance")]
[Authorize(Policy = Permissions.FinanceProfitView)]
public class FinanceController : ControllerBase
{
    private readonly FinanceService _financeService;

    public FinanceController(FinanceService financeService)
    {
        _financeService = financeService;
    }

    private static (DateTime From, DateTime To) ResolveRange(DateTime? from, DateTime? to)
    {
        var effectiveTo = to ?? DateTime.UtcNow.Date;
        var effectiveFrom = from ?? effectiveTo.AddDays(-29);
        return (effectiveFrom, effectiveTo);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<FinanceSummaryResponse>> GetSummary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var r = await _financeService.GetSummaryAsync(f, t);

        return Ok(new FinanceSummaryResponse(
            r.Revenue, r.Cogs, r.GrossProfit, r.OperatingExpenses, r.NetOperatingProfit,
            r.PaymentsReceived, r.AccountsReceivable, r.PurchaseSpend, r.CompletedOrdersCount,
            r.AverageOrderValue, r.GrossMarginPercent, r.NetOperatingMarginPercent, r.UnknownCostItemsCount));
    }

    [HttpGet("daily")]
    public async Task<ActionResult<List<DailyFinanceItem>>> GetDaily([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var r = await _financeService.GetDailyAsync(f, t);

        return Ok(r.Select(x => new DailyFinanceItem(
            x.Date, x.Revenue, x.Cogs, x.GrossProfit, x.Expenses, x.NetOperatingProfit, x.PaymentsReceived)).ToList());
    }

    [HttpGet("monthly")]
    public async Task<ActionResult<List<MonthlyFinanceItem>>> GetMonthly([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var r = await _financeService.GetMonthlyAsync(f, t);

        return Ok(r.Select(x => new MonthlyFinanceItem(
            x.Year, x.Month, x.Revenue, x.Cogs, x.GrossProfit, x.Expenses, x.NetOperatingProfit,
            x.PaymentsReceived, x.PurchaseSpend)).ToList());
    }

    [HttpGet("expenses-by-category")]
    public async Task<ActionResult<List<ExpenseCategoryBreakdownItem>>> GetExpensesByCategory(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var r = await _financeService.GetExpensesByCategoryAsync(f, t);

        return Ok(r.Select(x => new ExpenseCategoryBreakdownItem(x.Category, x.Amount, x.Percentage)).ToList());
    }

    [HttpGet("payments-by-method")]
    public async Task<ActionResult<List<PaymentMethodBreakdownItem>>> GetPaymentsByMethod(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var r = await _financeService.GetPaymentsByMethodAsync(f, t);

        return Ok(r.Select(x => new PaymentMethodBreakdownItem(x.Method, x.Amount, x.Count)).ToList());
    }

    [HttpGet("profit-by-product")]
    public async Task<ActionResult<FinancePagedResult<ProfitByProductItem>>> GetProfitByProduct(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string sortBy = "profit", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (f, t) = ResolveRange(from, to);
        var (items, totalItems) = await _financeService.GetProfitByProductAsync(f, t, sortBy, page, pageSize);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

                var mapped = items.Select(x => new ProfitByProductItem(
            x.ProductId, x.ProductName, x.Sku, x.QuantitySold, x.Revenue, x.Cogs ?? 0m,
            x.GrossProfit ?? 0m, x.GrossMarginPercent ?? 0m)).ToList();

        return Ok(new FinancePagedResult<ProfitByProductItem>(mapped, page, pageSize, totalItems, totalPages));
    }

    [HttpGet("profit-by-category")]
    public async Task<ActionResult<List<ProfitByCategoryItem>>> GetProfitByCategory(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var r = await _financeService.GetProfitByCategoryAsync(f, t);

                return Ok(r.Select(x => new ProfitByCategoryItem(
            x.Category, x.QuantitySold, x.Revenue, x.Cogs ?? 0m, x.GrossProfit ?? 0m, x.MarginPercent ?? 0m)).ToList());
    }
}