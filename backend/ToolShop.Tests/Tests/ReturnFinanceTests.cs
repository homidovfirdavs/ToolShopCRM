using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// TASK C3 - Finance / debts / reports after variant C: the sale stays in the period of the sale, the return is booked in the
/// period of the return (negative revenue allowed), damaged goods never touch healthy stock but stay in COGS as a memo write-off.
/// </summary>
[Collection("db")]
public class ReturnFinanceTests(ApiFactory factory) : TestBase(factory)
{
    private static string Range(int fromDays, int toDays) =>
        $"from={DateTime.UtcNow.Date.AddDays(fromDays):yyyy-MM-dd}&to={DateTime.UtcNow.Date.AddDays(toDays):yyyy-MM-dd}";

    private Task<JsonElement> SummaryAsync(int fromDays, int toDays) => GetJsonAsync($"/api/finance/summary?{Range(fromDays, toDays)}");

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var resp = await Admin.GetAsync(url);
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        return await resp.ReadJsonAsync();
    }

    private static object Line(int orderItemId, int quantity, int damaged = 0) =>
        new { orderItemId, quantity, damagedQuantity = damaged };

    private Task<HttpResponseMessage> ReturnAsync(int orderId, object? items = null) =>
        Admin.PostAsJsonAsync($"/api/orders/{orderId}/return", new { reason = "Customer returned goods", items });

    /// <summary>price 100, cost 60: sells 5 units 40 days ago (fully paid in cash), moves the sale AND its payment into the past.</summary>
    private async Task<(int OrderId, int ItemId, int ProductId)> OldSaleAsync(decimal? paid = null)
    {
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 20);
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 0m, Item(product, 5));
        await (await CompleteAsync(id, paid ?? total)).ShouldBeAsync(HttpStatusCode.OK);

        var past = DateTime.UtcNow.AddDays(-40);
        await Factory.WithDbAsync(async db =>
        {
            await db.Orders.Where(o => o.Id == id).ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDate, past));
            await db.Payments.Where(p => p.OrderId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, past));
        });
        var itemId = await Factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == id).Select(i => i.Id).SingleAsync());
        return (id, itemId, product);
    }

    // ---------- the three approved accounting examples (cost 60, price 100, sold 5, return 2 for 200) ----------

    [Fact]
    public async Task Example_A_returned_units_are_sellable_gross_profit_of_the_return_period_is_minus_80()
    {
        var (id, itemId, _) = await OldSaleAsync();
        await (await ReturnAsync(id, new[] { Line(itemId, 2) })).ShouldBeAsync(HttpStatusCode.OK);

        var today = await SummaryAsync(-1, 1);
        Assert.Equal(-200m, today.Dec("revenue"));
        Assert.Equal(-120m, today.Dec("cogs"));            // 2 restocked x 60 leave COGS
        Assert.Equal(-80m, today.Dec("grossProfit"));
        Assert.Equal(0m, today.Dec("writeOffCost"));
        Assert.Equal(-200m, today.Dec("paymentsReceived")); // refund paid out today
        Assert.Equal(200m, today.Dec("returnsValue"));
        Assert.Equal(200m, today.Dec("refundsPaid"));

        var old = await SummaryAsync(-45, -35);             // the sale period is untouched
        Assert.Equal(500m, old.Dec("revenue"));
        Assert.Equal(300m, old.Dec("cogs"));
        Assert.Equal(200m, old.Dec("grossProfit"));

        var all = await SummaryAsync(-45, 1);               // cumulative = sold 3 units: 300 revenue, 180 cost, 120 profit
        Assert.Equal(300m, all.Dec("revenue"));
        Assert.Equal(180m, all.Dec("cogs"));
        Assert.Equal(120m, all.Dec("grossProfit"));
    }

    [Fact]
    public async Task Example_B_returned_units_are_all_damaged_gross_profit_of_the_return_period_is_minus_200()
    {
        var (id, itemId, product) = await OldSaleAsync();
        var stockBefore = await StockAsync(product);
        await (await ReturnAsync(id, new[] { Line(itemId, 2, damaged: 2) })).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(stockBefore, await StockAsync(product));   // nothing went back to healthy stock

        var today = await SummaryAsync(-1, 1);
        Assert.Equal(-200m, today.Dec("revenue"));
        Assert.Equal(0m, today.Dec("cogs"));               // damaged cost stays in COGS: nothing reversed
        Assert.Equal(-200m, today.Dec("grossProfit"));
        Assert.Equal(120m, today.Dec("writeOffCost"));     // memo: 2 x 60 written off

        var all = await SummaryAsync(-45, 1);              // cumulative: 300 revenue, 300 cost -> 0 profit (3 sold units + 2 lost)
        Assert.Equal(300m, all.Dec("revenue"));
        Assert.Equal(300m, all.Dec("cogs"));
        Assert.Equal(0m, all.Dec("grossProfit"));
    }

    [Fact]
    public async Task Example_C_one_restocked_one_damaged_gross_profit_of_the_return_period_is_minus_140()
    {
        var (id, itemId, product) = await OldSaleAsync();
        var stockBefore = await StockAsync(product);
        await (await ReturnAsync(id, new[] { Line(itemId, 2, damaged: 1) })).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(stockBefore + 1, await StockAsync(product));

        var today = await SummaryAsync(-1, 1);
        Assert.Equal(-200m, today.Dec("revenue"));
        Assert.Equal(-60m, today.Dec("cogs"));
        Assert.Equal(-140m, today.Dec("grossProfit"));
        Assert.Equal(60m, today.Dec("writeOffCost"));

        var all = await SummaryAsync(-45, 1);
        Assert.Equal(300m, all.Dec("revenue"));
        Assert.Equal(240m, all.Dec("cogs"));
        Assert.Equal(60m, all.Dec("grossProfit"));
    }

    // ---------- unknown cost / N/A ----------

    [Fact]
    public async Task Return_of_a_unit_with_unknown_cost_marks_the_return_period_as_incomplete_not_zero()
    {
        var (id, itemId, _) = await OldSaleAsync();
        await Factory.WithDbAsync(db => db.OrderItems.Where(i => i.Id == itemId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.CostAtSale, (decimal?)null)));
        await (await ReturnAsync(id, new[] { Line(itemId, 2) })).ShouldBeAsync(HttpStatusCode.OK);

        var today = await SummaryAsync(-1, 1);
        Assert.True(today.Int("unknownCostItemsCount") >= 1);   // UI shows N/A, never an invented 0

        var old = await SummaryAsync(-45, -35);
        Assert.Equal(1, old.Int("unknownCostItemsCount"));
    }

    // ---------- debts / receivables ----------

    [Fact]
    public async Task Partially_returned_credit_order_stays_a_debt_for_the_remaining_amount_everywhere()
    {
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 20);
        var customer = await SeedCustomerAsync();
        var (id, _) = await CreateOrderAsync(customer, 0m, Item(product, 5));
        await (await CompleteAsync(id, 200m)).ShouldBeAsync(HttpStatusCode.OK);              // total 500, paid 200, debt 300
        var itemId = await Factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == id).Select(i => i.Id).SingleAsync());

        await (await ReturnAsync(id, new[] { Line(itemId, 2) })).ShouldBeAsync(HttpStatusCode.OK); // goods 200 -> debt 100

        Assert.Equal(100m, (await SummaryAsync(-1, 1)).Dec("accountsReceivable"));
        Assert.Equal(100m, (await GetJsonAsync("/api/dashboard/summary")).Dec("accountsReceivable"));

        var debts = await GetJsonAsync("/api/debts?pageSize=100");
        var row = debts.GetProperty("items").EnumerateArray().Single(i => i.Int("customerId") == customer);
        Assert.Equal(100m, row.Dec("totalDebt"));
        Assert.Equal(1, row.Int("outstandingOrders"));

        var debtReport = await GetJsonAsync("/api/reports/customer-debt?pageSize=100");
        Assert.Contains(debtReport.GetProperty("items").EnumerateArray(), i => i.Int("customerId") == customer && i.Dec("totalDebt") == 100m);

        var outstanding = await GetJsonAsync($"/api/customers/{customer}/outstanding-orders");
        Assert.Equal(100m, outstanding.EnumerateArray().Single().Dec("outstanding"));

        // return the rest -> the debt disappears
        await (await ReturnAsync(id)).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(0m, (await SummaryAsync(-1, 1)).Dec("accountsReceivable"));
        var after = await GetJsonAsync("/api/debts?pageSize=100");
        Assert.DoesNotContain(after.GetProperty("items").EnumerateArray(), i => i.Int("customerId") == customer);
    }

    // ---------- reports ----------

    [Fact]
    public async Task Returns_report_lists_each_return_with_its_totals_and_unknown_cost_is_null()
    {
        var (id, itemId, _) = await OldSaleAsync();
        await (await ReturnAsync(id, new[] { Line(itemId, 1) })).ShouldBeAsync(HttpStatusCode.OK);
        await (await ReturnAsync(id, new[] { Line(itemId, 2, damaged: 1) })).ShouldBeAsync(HttpStatusCode.OK);

        var rep = await GetJsonAsync($"/api/reports/returns?{Range(-1, 1)}");
        Assert.Equal(2, rep.Int("returnsCount"));
        Assert.Equal(300m, rep.Dec("goodsValue"));
        Assert.Equal(300m, rep.Dec("refundsPaid"));
        Assert.Equal(120m, rep.Dec("costReversed"));       // 60 + 60 (restocked units)
        Assert.Equal(60m, rep.Dec("writeOffCost"));
        var rows = rep.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.StartsWith("RET-", r.Str("returnNumber")));

        await Factory.WithDbAsync(db => db.OrderReturnItems.ExecuteUpdateAsync(s => s.SetProperty(i => i.CostAtSaleSnapshot, (decimal?)null)));
        await Factory.WithDbAsync(db => db.OrderReturns.ExecuteUpdateAsync(s => s.SetProperty(r => r.CostReversed, (decimal?)null)));
        var unknown = await GetJsonAsync($"/api/reports/returns?{Range(-1, 1)}");
        Assert.Equal(JsonValueKind.Null, unknown.GetProperty("costReversed").ValueKind);   // N/A, not 0
    }

    [Fact]
    public async Task Payments_by_method_subtracts_refunds_of_the_period()
    {
        var (id, itemId, _) = await OldSaleAsync();
        await (await ReturnAsync(id, new[] { Line(itemId, 2) })).ShouldBeAsync(HttpStatusCode.OK);   // refund 200 in Cash

        var methods = await GetJsonAsync($"/api/finance/payments-by-method?{Range(-1, 1)}");
        var cash = methods.EnumerateArray().Single(m => m.Str("method") == "Cash");
        Assert.Equal(-200m, cash.Dec("amount"));
    }

    [Fact]
    public async Task Daily_finance_shows_the_return_on_the_day_it_happened()
    {
        var (id, itemId, _) = await OldSaleAsync();
        await (await ReturnAsync(id, new[] { Line(itemId, 2) })).ShouldBeAsync(HttpStatusCode.OK);

        var daily = await GetJsonAsync($"/api/finance/daily?{Range(-1, 0)}");
        var todayRow = daily.EnumerateArray().Last();
        Assert.Equal(-200m, todayRow.Dec("revenue"));
        Assert.Equal(-120m, todayRow.Dec("cogs"));
        Assert.Equal(-80m, todayRow.Dec("grossProfit"));
        Assert.Equal(-200m, todayRow.Dec("paymentsReceived"));
    }

    [Fact]
    public async Task Profit_by_product_has_negative_lines_for_returns_in_the_return_period()
    {
        var (id, itemId, product) = await OldSaleAsync();
        await (await ReturnAsync(id, new[] { Line(itemId, 2) })).ShouldBeAsync(HttpStatusCode.OK);

        var rows = await GetJsonAsync($"/api/finance/profit-by-product?{Range(-1, 1)}");
        var row = rows.GetProperty("items").EnumerateArray().Single(r => r.Int("productId") == product);
        Assert.Equal(-2, row.Int("quantitySold"));
        Assert.Equal(-200m, row.Dec("revenue"));
        Assert.Equal(-120m, row.Dec("cogs"));
        Assert.Equal(-80m, row.Dec("grossProfit"));
    }

    // ---------- reconciliation: the numbers must agree with the raw ledger ----------

    [Fact]
    public async Task Reconciliation_finance_numbers_equal_the_raw_ledger_after_a_mix_of_operations()
    {
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 100);
        var customerA = await SeedCustomerAsync();
        var customerB = await SeedCustomerAsync();

        var (o1, t1) = await CreateOrderAsync(customerA, 10m, Item(product, 4, 5m));   // paid in full
        await (await CompleteAsync(o1, t1)).ShouldBeAsync(HttpStatusCode.OK);
        var (o2, _) = await CreateOrderAsync(customerB, 0m, Item(product, 6));         // credit, paid 100
        await (await CompleteAsync(o2, 100m)).ShouldBeAsync(HttpStatusCode.OK);
        var (o3, t3) = await CreateOrderAsync(null, 0m, Item(product, 3));             // walk-in
        await (await CompleteAsync(o3, t3)).ShouldBeAsync(HttpStatusCode.OK);

        var i1 = await Factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == o1).Select(i => i.Id).SingleAsync());
        var i2 = await Factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == o2).Select(i => i.Id).SingleAsync());
        var i3 = await Factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == o3).Select(i => i.Id).SingleAsync());

        await (await ReturnAsync(o1, new[] { Line(i1, 1) })).ShouldBeAsync(HttpStatusCode.OK);
        await (await ReturnAsync(o2, new[] { Line(i2, 2, damaged: 1) })).ShouldBeAsync(HttpStatusCode.OK);
        await (await ReturnAsync(o3)).ShouldBeAsync(HttpStatusCode.OK);                 // whole walk-in order
        await (await ReturnAsync(o1, new[] { Line(i1, 3) })).ShouldBeAsync(HttpStatusCode.OK);   // finishes o1

        var s = await SummaryAsync(-1, 1);

        var raw = await Factory.WithDbAsync(async db =>
        {
            var orders = await db.Orders.AsNoTracking().Include(o => o.Items).ToListAsync();
            var returns = await db.OrderReturns.AsNoTracking().Include(r => r.Items).ToListAsync();
            var payments = await db.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Completed).ToListAsync();

            var revenue = orders.Sum(o => o.TotalAmount) - returns.Sum(r => r.GoodsValue);
            var sold = orders.SelectMany(o => o.Items).Sum(i => i.Quantity * i.CostAtSale!.Value);
            var reversed = returns.SelectMany(r => r.Items).Sum(i => i.RestockedQuantity * i.CostAtSaleSnapshot!.Value);
            var received = payments.Sum(p => p.Amount) - returns.Sum(r => r.RefundAmount);
            var receivable = orders.Sum(o => Math.Max(0m,
                o.TotalAmount - returns.Where(r => r.OrderId == o.Id).Sum(r => r.GoodsValue)
                - (payments.Where(p => p.OrderId == o.Id).Sum(p => p.Amount) - returns.Where(r => r.OrderId == o.Id).Sum(r => r.RefundAmount))));
            var writeOff = returns.SelectMany(r => r.Items).Sum(i => i.WrittenOffQuantity * i.CostAtSaleSnapshot!.Value);
            return (revenue, cogs: sold - reversed, received, receivable, writeOff);
        });

        Assert.Equal(raw.revenue, s.Dec("revenue"));
        Assert.Equal(raw.cogs, s.Dec("cogs"));
        Assert.Equal(raw.received, s.Dec("paymentsReceived"));
        Assert.Equal(raw.receivable, s.Dec("accountsReceivable"));
        Assert.Equal(raw.writeOff, s.Dec("writeOffCost"));
        Assert.Equal(raw.revenue - raw.cogs, s.Dec("grossProfit"));

        // Returns per order never exceed what was sold, and a finished order returned exactly its total.
        var o1Returned = await Factory.WithDbAsync(db => db.OrderReturns.Where(r => r.OrderId == o1).SumAsync(r => r.GoodsValue));
        Assert.Equal(t1, o1Returned);
        Assert.Equal(OrderStatus.Returned, await OrderStatusAsync(o1));
        Assert.Equal(OrderStatus.PartiallyReturned, await OrderStatusAsync(o2));
    }
}
