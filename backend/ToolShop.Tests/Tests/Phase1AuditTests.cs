using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// PHASE 1 audit evidence tests (F1, F4, F5, F6, F10). Tests ONLY - no production code is changed by this file.
/// A test named "Gap_..." or whose assertion states the DESIRED behaviour is EXPECTED TO FAIL until the fix is made;
/// a test named "Today_..." documents current behaviour and passes now.
/// </summary>
[Collection("db")]
public class Phase1AuditTests(ApiFactory factory) : TestBase(factory)
{
    private static bool Ok(HttpResponseMessage r) => (int)r.StatusCode is >= 200 and < 300;
    private static bool ServerError(HttpResponseMessage r) => (int)r.StatusCode >= 500;

    private static void AssertNo5xx(IEnumerable<HttpResponseMessage> results)
    {
        if (results.Any(ServerError))
            throw new Xunit.Sdk.XunitException("HTTP 5xx. Server error log:\n" + ErrorCaptureProvider.Dump());
    }

    private Task<HttpResponseMessage> CancelAsync(int orderId, HttpClient? c = null) =>
        (c ?? Admin).PostAsync($"/api/orders/{orderId}/cancel", null);

    // =================== F1: concurrency (payments / complete) ===================

    [Fact]
    public async Task F1_two_parallel_payments_never_overpay_and_never_return_5xx()
    {
        const int rounds = 12;
        for (var i = 0; i < rounds; i++)
        {
            var product = await SeedProductAsync(salePrice: 100m, stock: 10);
            var customer = await SeedCustomerAsync();
            var (id, total) = await CreateCompletedCreditOrderAsync(customer, product, 5); // 500 owed
            Assert.Equal(500m, total);

            var results = await Task.WhenAll(PayAsync(id, 300m, Admin), PayAsync(id, 300m, Manager));

            AssertNo5xx(results);                 // F1: must be 409, never 500
            Assert.Equal(1, results.Count(Ok));                          // 300 + 300 > 500, only one may win
            Assert.True(await PaidSumAsync(id) <= total, $"round {i}: order was overpaid");
            Assert.Equal(1, await Factory.WithDbAsync(db =>
                db.Payments.CountAsync(p => p.OrderId == id && p.Status == PaymentStatus.Completed)));
        }
    }

    [Fact]
    public async Task F1_two_parallel_completes_of_the_same_draft_sell_once()
    {
        const int rounds = 12;
        for (var i = 0; i < rounds; i++)
        {
            var product = await SeedProductAsync(salePrice: 100m, stock: 10);
            var customer = await SeedCustomerAsync();
            var (id, _) = await CreateOrderAsync(customer, 0m, Item(product, 3));

            var results = await Task.WhenAll(CompleteAsync(id, null, Admin), CompleteAsync(id, null, Manager));

            AssertNo5xx(results);
            Assert.Equal(1, results.Count(Ok));                                       // no duplicate sale
            Assert.Equal(7, await StockAsync(product));                                // stock taken exactly once
            Assert.Equal(1, await Factory.WithDbAsync(db =>
                db.InventoryMovements.CountAsync(m => m.ReferenceId == id && m.MovementType == ToolShop.Domain.Entities.Inventory.InventoryMovementType.Sale)));
        }
    }

    [Fact]
    public async Task F1_two_devices_selling_the_last_unit_never_oversell()
    {
        const int rounds = 12;
        for (var i = 0; i < rounds; i++)
        {
            var product = await SeedProductAsync(salePrice: 100m, stock: 1);
            var customer = await SeedCustomerAsync();
            var (a, _) = await CreateOrderAsync(customer, 0m, Item(product, 1));
            var (b, _) = await CreateOrderAsync(customer, 0m, Item(product, 1));

            var results = await Task.WhenAll(CompleteAsync(a, null, Admin), CompleteAsync(b, null, Manager));

            AssertNo5xx(results);   // F1: loser must get 409 (insufficient stock), not 500
            Assert.Equal(1, results.Count(Ok));
            Assert.Equal(0, await StockAsync(product));    // never negative
        }
    }

    [Fact]
    public async Task F1_two_separate_payments_with_different_keys_are_both_recorded()
    {
        // Idempotency protects ONE user intention (one key). Two intentions (two keys) are two payments, as long as the order
        // still has an outstanding balance. Retrying the SAME intention is covered by IdempotentPaymentTests.
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();
        var (id, _) = await CreateCompletedCreditOrderAsync(customer, product, 8); // 800 owed

        await (await PayAsync(id, 300m)).ShouldBeAsync(HttpStatusCode.Created);
        await (await PayAsync(id, 300m)).ShouldBeAsync(HttpStatusCode.Created);
        Assert.Equal(600m, await PaidSumAsync(id));
    }

    // =================== F4: order date on Draft vs Complete ===================

    [Fact]
    public async Task F4_completing_an_old_draft_sets_the_order_date_to_the_completion_time()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();
        var (id, _) = await CreateOrderAsync(customer, 0m, Item(product, 2));

        // the draft was held two days ago
        var old = DateTime.UtcNow.AddDays(-2);
        await Factory.WithDbAsync(db => db.Orders.Where(o => o.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDate, old)));

        await (await CompleteAsync(id)).ShouldBeAsync(HttpStatusCode.OK);

        var (orderDate, completedAt) = await Factory.WithDbAsync(async db =>
        {
            var o = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == id);
            return (o.OrderDate, o.CompletedAt!.Value);
        });
        // Revenue/COGS are bucketed by OrderDate while payments use their own date: the sale date must be the completion time.
        Assert.True((completedAt - orderDate).Duration() < TimeSpan.FromMinutes(1),
            $"OrderDate {orderDate:o} differs from CompletedAt {completedAt:o} - revenue lands on the day the draft was created.");
    }

    // =================== F5: CancelDraft integrity ===================

    [Fact]
    public async Task F5_cancel_of_a_completed_order_is_refused_and_changes_nothing()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 5);
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 0m, Item(product, 2));
        await (await CompleteAsync(id, total)).ShouldBeAsync(HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(id)).StatusCode);
        Assert.Equal(OrderStatus.Completed, await OrderStatusAsync(id));
        Assert.Equal(3, await StockAsync(product));
    }

    [Fact]
    public async Task F5_parallel_cancel_and_complete_never_leave_a_cancelled_order_with_stock_taken()
    {
        // The race window is small, so a PASS here does not prove the bug is absent; a FAIL proves it.
        const int rounds = 30;
        for (var i = 0; i < rounds; i++)
        {
            var product = await SeedProductAsync(salePrice: 100m, stock: 10);
            var customer = await SeedCustomerAsync();
            var (id, total) = await CreateOrderAsync(customer, 0m, Item(product, 2));

            var results = await Task.WhenAll(CancelAsync(id, Admin), CompleteAsync(id, total, Manager));
            AssertNo5xx(results);

            var status = await OrderStatusAsync(id);
            var stock = await StockAsync(product);
            var paid = await PaidSumAsync(id);

            if (status == OrderStatus.Cancelled)
            {
                Assert.True(stock == 10 && paid == 0m,
                    $"round {i}: order is Cancelled but stock={stock} and paid={paid} (sale side effects survived).");
            }
            else
            {
                Assert.Equal(OrderStatus.Completed, status);
                Assert.Equal(8, stock);
            }
        }
    }

    // =================== F6: RBAC / confidential data ===================

    [Fact]
    public async Task F6_cashier_does_not_get_purchase_price_in_product_responses()
    {
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 3);

        var one = await Cashier.GetAsync($"/api/products/{product}");
        await one.ShouldBeAsync(HttpStatusCode.OK);
        AssertNoCost(await one.ReadJsonAsync());

        var list = await Cashier.GetAsync("/api/products?pageSize=50");
        await list.ShouldBeAsync(HttpStatusCode.OK);
        foreach (var item in (await list.ReadJsonAsync()).GetProperty("items").EnumerateArray())
            AssertNoCost(item);
    }

    [Fact]
    public async Task F6_manager_and_admin_still_see_purchase_price()
    {
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 3);
        foreach (var c in new[] { Admin, Manager })
        {
            var r = await c.GetAsync($"/api/products/{product}");
            await r.ShouldBeAsync(HttpStatusCode.OK);
            Assert.Equal(60m, (await r.ReadJsonAsync()).Dec("purchasePrice"));
        }
    }

    [Theory]
    [InlineData("/api/purchases")]
    [InlineData("/api/suppliers")]
    [InlineData("/api/inventory/movements")]
    public async Task F6_cashier_cannot_read_purchases_suppliers_or_movements(string url)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Cashier.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("/api/purchases")]
    [InlineData("/api/suppliers")]
    [InlineData("/api/inventory/movements")]
    public async Task F6_manager_admin_and_warehouse_can_still_read_them(string url)
    {
        foreach (var c in new[] { Admin, Manager, Warehouse })
            await (await c.GetAsync(url)).ShouldBeAsync(HttpStatusCode.OK);
    }

    private static void AssertNoCost(JsonElement e)
    {
        if (!e.TryGetProperty("purchasePrice", out var v)) return;            // property removed - good
        Assert.Equal(JsonValueKind.Null, v.ValueKind);                         // null (unknown to this role), never a real number
    }

    // =================== F10: return / refund impact ===================

    [Fact]
    public async Task Return_is_booked_in_the_period_of_the_RETURN_and_the_sale_stays_in_its_own_period()
    {
        // F10 fixed by variant C (C2 ledger + C3 formulas): a return no longer rewrites the period of the original sale.
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 10);
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 0m, Item(product, 5));
        await (await CompleteAsync(id, total)).ShouldBeAsync(HttpStatusCode.OK);

        var past = DateTime.UtcNow.AddDays(-40);
        await Factory.WithDbAsync(async db =>
        {
            await db.Orders.Where(o => o.Id == id).ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDate, past));
            await db.Payments.Where(p => p.OrderId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, past));
        });

        string Range(int fromDays, int toDays) =>
            $"/api/finance/summary?from={DateTime.UtcNow.Date.AddDays(fromDays):yyyy-MM-dd}&to={DateTime.UtcNow.Date.AddDays(toDays):yyyy-MM-dd}";

        var oldBefore = await (await Admin.GetAsync(Range(-45, -35))).ReadJsonAsync();
        Assert.Equal(500m, oldBefore.Dec("revenue"));
        Assert.Equal(500m, oldBefore.Dec("paymentsReceived"));

        await (await Admin.PostAsJsonAsync($"/api/orders/{id}/return", new { reason = "test" })).ShouldBeAsync(HttpStatusCode.OK);

        var oldAfter = await (await Admin.GetAsync(Range(-45, -35))).ReadJsonAsync();
        var todayAfter = await (await Admin.GetAsync(Range(-1, 1))).ReadJsonAsync();
        Assert.Equal(500m, oldAfter.Dec("revenue"));             // the closed period is NOT rewritten
        Assert.Equal(300m, oldAfter.Dec("cogs"));
        Assert.Equal(500m, oldAfter.Dec("paymentsReceived"));
        Assert.Equal(-500m, todayAfter.Dec("revenue"));          // the return shows up in the period it happened
        Assert.Equal(-300m, todayAfter.Dec("cogs"));
        Assert.Equal(-500m, todayAfter.Dec("paymentsReceived")); // refund outflow
        Assert.Equal(10, await StockAsync(product));             // stock itself is correct
    }
}
