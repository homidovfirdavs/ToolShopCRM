using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// Full-order return: completed order -> Returned, stock goes back through a SaleReturn movement,
/// valid payments become Refunded, and the order disappears from revenue, COGS, receivables and debts.
/// </summary>
[Collection("db")]
public class ReturnTests(ApiFactory factory) : TestBase(factory)
{
    private Task<HttpResponseMessage> ReturnOrderAsync(int id, string reason = "Customer changed their mind", HttpClient? client = null) =>
        (client ?? Admin).PostAsJsonAsync($"/api/orders/{id}/return", new { reason });

    private async Task<System.Text.Json.JsonElement> FinanceAsync()
    {
        var resp = await Admin.GetAsync("/api/finance/summary");
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        return await resp.ReadJsonAsync();
    }

    // ---------- permissions ----------

    [Fact]
    public async Task Only_admin_and_manager_can_return()
    {
        var product = await SeedProductAsync(stock: 10);
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 0m, Item(product, 2));
        await (await CompleteAsync(id, total)).ShouldBeAsync(HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ReturnOrderAsync(id, client: Anonymous)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ReturnOrderAsync(id, client: Cashier)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ReturnOrderAsync(id, client: Warehouse)).StatusCode);
        Assert.Equal(OrderStatus.Completed, await OrderStatusAsync(id));

        await (await ReturnOrderAsync(id, client: Manager)).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(OrderStatus.Returned, await OrderStatusAsync(id));
    }

    // ---------- stock + money ----------

    [Fact]
    public async Task Return_of_a_paid_sale_restores_stock_and_refunds_the_payment()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 0m, Item(product, 4));
        await (await CompleteAsync(id, total)).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(6, await StockAsync(product));
        Assert.Equal(total, await PaidSumAsync(id));

        var movementsBefore = await MovementCountAsync();
        var resp = await ReturnOrderAsync(id, "Wrong model");
        await resp.ShouldBeAsync(HttpStatusCode.OK);

        var json = await resp.ReadJsonAsync();
        Assert.Equal("Returned", json.Str("status"));
        Assert.Equal("Wrong model", json.Str("returnReason"));
        Assert.Equal("t_admin", json.Str("returnedByUsername"));
        Assert.Equal(total, json.Dec("refundedAmount"));

        Assert.Equal(10, await StockAsync(product));                  // stock is back
        Assert.Equal(movementsBefore + 1, await MovementCountAsync()); // exactly one SaleReturn line
        // C2: payments are NOT flipped any more - the refund is a ledger row, the payment keeps its history.
        Assert.Equal(total, await PaidSumAsync(id));
        var ledger = await Factory.WithDbAsync(db => db.OrderReturns.SingleAsync(r => r.OrderId == id));
        Assert.Equal(total, ledger.GoodsValue);
        Assert.Equal(total, ledger.RefundAmount);
        Assert.Equal(PaymentMethod.Cash, ledger.RefundMethod);
        var returnMovements = await Factory.WithDbAsync(db =>
            db.InventoryMovements.CountAsync(m => m.ReferenceId == id && m.MovementType == InventoryMovementType.SaleReturn));
        Assert.Equal(1, returnMovements);
    }

    [Fact]
    public async Task Return_of_a_partly_paid_credit_sale_clears_the_debt_and_the_finance_numbers()
    {
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 20);
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 0m, Item(product, 5));
        Assert.Equal(500m, total);
        await (await CompleteAsync(id, 200m)).ShouldBeAsync(HttpStatusCode.OK);

        var before = await FinanceAsync();
        Assert.Equal(500m, before.Dec("revenue"));
        Assert.Equal(300m, before.Dec("accountsReceivable"));
        Assert.Equal(200m, before.Dec("paymentsReceived"));
        Assert.Equal(300m, before.Dec("cogs"));

        await (await ReturnOrderAsync(id)).ShouldBeAsync(HttpStatusCode.OK);

        var after = await FinanceAsync();
        Assert.Equal(0m, after.Dec("revenue"));
        Assert.Equal(0m, after.Dec("cogs"));
        Assert.Equal(0m, after.Dec("accountsReceivable"));
        Assert.Equal(0m, after.Dec("paymentsReceived"));   // 200 received - 200 refunded in the same period
        Assert.Equal(1, after.Int("completedOrdersCount")); // the sale itself still happened (it is a count of sales made)

        var dash = await Admin.GetAsync("/api/dashboard/summary");
        await dash.ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(0m, (await dash.ReadJsonAsync()).Dec("accountsReceivable"));

        var debts = await (await Admin.GetAsync("/api/debts?pageSize=100")).ReadJsonAsync();
        Assert.DoesNotContain(debts.GetProperty("items").EnumerateArray(), i => i.Int("customerId") == customer);

        var payments = await (await Admin.GetAsync($"/api/orders/{id}/payments")).ReadJsonAsync();
        Assert.Equal("Refunded", payments.Str("paymentState"));
        Assert.Equal(0m, payments.Dec("outstandingBalance"));
        Assert.Equal(200m, payments.Dec("refundedTotal"));
    }

    [Fact]
    public async Task Walk_in_sale_can_be_returned()
    {
        var product = await SeedProductAsync(salePrice: 50m, stock: 5);
        var (id, total) = await CreateOrderAsync(null, 0m, Item(product, 2));
        await (await CompleteAsync(id, total)).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(3, await StockAsync(product));

        await (await ReturnOrderAsync(id)).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(5, await StockAsync(product));
        var ledger = await Factory.WithDbAsync(db => db.OrderReturns.SingleAsync(r => r.OrderId == id));
        Assert.Equal(total, ledger.RefundAmount);   // walk-in was fully paid -> fully refunded
    }

    [Fact]
    public async Task Return_keeps_historical_prices_discounts_and_cost_snapshot()
    {
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 10);
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 30m, Item(product, 3, 10m));
        await (await CompleteAsync(id, total)).ShouldBeAsync(HttpStatusCode.OK);

        var before = await Factory.WithDbAsync(async db =>
        {
            var o = await db.Orders.AsNoTracking().Include(x => x.Items).FirstAsync(x => x.Id == id);
            return (o.TotalAmount, o.DiscountAmount, o.Items.Select(i => (i.UnitPrice, i.DiscountAmount, i.LineTotal, i.CostAtSale)).ToList());
        });
        Assert.All(before.Item3, i => Assert.NotNull(i.CostAtSale));

        await (await ReturnOrderAsync(id)).ShouldBeAsync(HttpStatusCode.OK);

        var after = await Factory.WithDbAsync(async db =>
        {
            var o = await db.Orders.AsNoTracking().Include(x => x.Items).FirstAsync(x => x.Id == id);
            return (o.TotalAmount, o.DiscountAmount, o.Items.Select(i => (i.UnitPrice, i.DiscountAmount, i.LineTotal, i.CostAtSale)).ToList());
        });
        Assert.Equal(before.TotalAmount, after.TotalAmount);
        Assert.Equal(before.DiscountAmount, after.DiscountAmount);
        Assert.Equal(before.Item3, after.Item3);
    }

    // ---------- rules ----------

    [Fact]
    public async Task Only_completed_orders_can_be_returned_and_only_once()
    {
        var product = await SeedProductAsync(stock: 10);
        var customer = await SeedCustomerAsync();

        var (draft, _) = await CreateOrderAsync(customer, 0m, Item(product, 1));
        var r1 = await ReturnOrderAsync(draft);
        await r1.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("ORDER_NOT_RETURNABLE", (await r1.ReadJsonAsync()).Str("code"));

        var (cancelled, _) = await CreateOrderAsync(customer, 0m, Item(product, 1));
        await (await Admin.PostAsync($"/api/orders/{cancelled}/cancel", null)).ShouldBeAsync(HttpStatusCode.NoContent);
        await (await ReturnOrderAsync(cancelled)).ShouldBeAsync(HttpStatusCode.Conflict);

        var (done, total) = await CreateOrderAsync(customer, 0m, Item(product, 2));
        await (await CompleteAsync(done, total)).ShouldBeAsync(HttpStatusCode.OK);
        await (await ReturnOrderAsync(done)).ShouldBeAsync(HttpStatusCode.OK);
        var stockAfterFirst = await StockAsync(product);

        await (await ReturnOrderAsync(done)).ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal(stockAfterFirst, await StockAsync(product)); // a second return must not add stock again

        await (await ReturnOrderAsync(999_999)).ShouldBeAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_reason_is_required()
    {
        var product = await SeedProductAsync(stock: 10);
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 0m, Item(product, 1));
        await (await CompleteAsync(id, total)).ShouldBeAsync(HttpStatusCode.OK);

        await (await ReturnOrderAsync(id, "")).ShouldBeAsync(HttpStatusCode.BadRequest);
        await (await ReturnOrderAsync(id, "ab")).ShouldBeAsync(HttpStatusCode.BadRequest);
        await (await ReturnOrderAsync(id, new string('x', 501))).ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal(OrderStatus.Completed, await OrderStatusAsync(id));
        Assert.Equal(9, await StockAsync(product)); // nothing changed
    }

    [Fact]
    public async Task A_returned_order_cannot_be_paid_again()
    {
        var product = await SeedProductAsync(stock: 10);
        var customer = await SeedCustomerAsync();
        var (id, _) = await CreateCompletedCreditOrderAsync(customer, product, 2);
        await (await ReturnOrderAsync(id)).ShouldBeAsync(HttpStatusCode.OK);

        var pay = await PayAsync(id, 10m);
        Assert.Equal(HttpStatusCode.BadRequest, pay.StatusCode);
        Assert.Equal(0, await PaymentCountAsync());
    }

    [Fact]
    public async Task Return_is_audited()
    {
        var product = await SeedProductAsync(stock: 10);
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 0m, Item(product, 1));
        await (await CompleteAsync(id, total)).ShouldBeAsync(HttpStatusCode.OK);
        await (await ReturnOrderAsync(id, "Defective")).ShouldBeAsync(HttpStatusCode.OK);

        var log = await Factory.WithDbAsync(db =>
            db.AuditLogs.SingleAsync(a => a.Action == "Order.Return" && a.EntityId == id.ToString()));
        Assert.Equal(Factory.AdminUserId, log.UserId);
        Assert.Contains("Defective", log.Description);
    }
}
