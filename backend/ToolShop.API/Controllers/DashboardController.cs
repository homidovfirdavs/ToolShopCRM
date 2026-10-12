using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToolShop.API.DTOs.Dashboard;
using ToolShop.Domain.Authorization;
using ToolShop.Infrastructure.Dashboard;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Permissions.FinanceProfitView)]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboardService;

    public DashboardController(DashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    private static (DateTime From, DateTime To) ResolveRange(DateTime? from, DateTime? to)
    {
        var effectiveTo = to ?? DateTime.UtcNow.Date;
        var effectiveFrom = from ?? effectiveTo.AddDays(-29);
        return (effectiveFrom, effectiveTo);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryResponse>> GetSummary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var r = await _dashboardService.GetSummaryAsync(f, t);

        return Ok(new DashboardSummaryResponse(
            r.DateFrom, r.DateTo, r.SalesRevenue, r.GrossProfit, r.PaymentsReceived,
            r.AccountsReceivable, r.LowStockCount, r.OperatingExpenses, r.HasCompleteCostData,
            r.OutOfStockCount, r.CompletedOrdersCount));
    }

    [HttpGet("sales-trend")]
    public async Task<ActionResult<List<SalesTrendPointResponse>>> GetSalesTrend([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var r = await _dashboardService.GetSalesTrendAsync(f, t);
        return Ok(r.Select(x => new SalesTrendPointResponse(x.Date, x.Revenue, x.GrossProfit, x.HasCompleteCostData)).ToList());
    }

    [HttpGet("sales-by-category")]
    public async Task<ActionResult<List<CategoryBreakdownResponse>>> GetSalesByCategory([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var r = await _dashboardService.GetSalesByCategoryAsync(f, t);
        return Ok(r.Select(x => new CategoryBreakdownResponse(x.Category, x.Revenue, x.Percentage)).ToList());
    }

    [HttpGet("attention")]
    public async Task<ActionResult<List<AttentionItemResponse>>> GetAttention([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var r = await _dashboardService.GetAttentionAsync(f, t);
        return Ok(r.Select(x => new AttentionItemResponse(x.Type, x.Label, x.Count, x.Amount, x.Link)).ToList());
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<List<LowStockItemResponse>>> GetLowStock([FromQuery] int top = 10)
    {
        var r = await _dashboardService.GetLowStockProductsAsync(top);
        return Ok(r.Select(x => new LowStockItemResponse(x.ProductId, x.ProductName, x.Sku, x.CurrentStock, x.MinimumStockLevel, x.Status)).ToList());
    }

    [HttpGet("financial-summary")]
    public async Task<ActionResult<FinancialSummaryShortResponse>> GetFinancialSummary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = ResolveRange(from, to);
        var r = await _dashboardService.GetFinancialSummaryShortAsync(f, t);
        return Ok(new FinancialSummaryShortResponse(r.Revenue, r.Cogs, r.GrossProfit, r.OperatingExpenses, r.NetOperatingProfit, r.HasCompleteCostData));
    }
}