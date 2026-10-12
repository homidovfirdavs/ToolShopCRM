using System.Net;
using System.Net.Http.Json;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// Backend rule: a walk-in order (no customer) must be fully paid when it is completed. Partial payment and credit
/// need an identified customer. The rule must hold even if the frontend is bypassed.
/// </summary>
[Collection("db")]
public class WalkInTests(ApiFactory factory) : TestBase(factory)
{
    private async Task AssertNothingChangedAsync(int orderId, int productId, int expectedStock)
    {
        Assert.Equal(OrderStatus.Draft, await OrderStatusAsync(orderId));
        Assert.Equal(expectedStock, await StockAsync(productId));
        Assert.Equal(0, await PaymentCountAsync());
    }

    [Fact]
    public async Task Walk_in_sale_without_payment_is_rejected_atomically()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var (orderId, _) = await CreateOrderAsync(null, 0m, Item(product, 1));
        var movements = await MovementCountAsync();

        var resp = await CompleteAsync(orderId);
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("WALKIN_FULL_PAYMENT_REQUIRED", (await resp.ReadJsonAsync()).Str("code"));

        await AssertNothingChangedAsync(orderId, product, 10);
        Assert.Equal(movements, await MovementCountAsync());
    }

    [Fact]
    public async Task Walk_in_sale_with_partial_payment_is_rejected_atomically()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var (orderId, _) = await CreateOrderAsync(null, 0m, Item(product, 1)); // total 100

        var resp = await CompleteAsync(orderId, initialPayment: 99.99m);
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("WALKIN_FULL_PAYMENT_REQUIRED", (await resp.ReadJsonAsync()).Str("code"));

        await AssertNothingChangedAsync(orderId, product, 10);
    }

    [Fact]
    public async Task Walk_in_sale_with_full_payment_completes_payment_and_stock_together()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var (orderId, total) = await CreateOrderAsync(null, 0m, Item(product, 2)); // 200

        await (await CompleteAsync(orderId, initialPayment: total)).ShouldBeAsync(HttpStatusCode.OK);

        Assert.Equal(OrderStatus.Completed, await OrderStatusAsync(orderId));
        Assert.Equal(200m, await PaidSumAsync(orderId));
        Assert.Equal(1, await PaymentCountAsync());
        Assert.Equal(8, await StockAsync(product));
    }

    [Fact]
    public async Task Overpaying_at_completion_is_rejected_atomically()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var (orderId, _) = await CreateOrderAsync(null, 0m, Item(product, 1)); // 100

        var resp = await CompleteAsync(orderId, initialPayment: 150m);
        await resp.ShouldBeAsync(HttpStatusCode.Conflict);
        var json = await resp.ReadJsonAsync();
        Assert.Equal("PAYMENT_EXCEEDS_TOTAL", json.Str("code"));
        Assert.Equal(100m, json.Dec("outstanding"));
        Assert.Equal(150m, json.Dec("requested"));

        await AssertNothingChangedAsync(orderId, product, 10);
    }

    [Fact]
    public async Task Order_level_discount_is_applied_before_the_full_payment_check()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var (orderId, total) = await CreateOrderAsync(null, 50m, Item(product, 2)); // 200 - 50 = 150
        Assert.Equal(150m, total);

        await (await CompleteAsync(orderId, initialPayment: 149m)).ShouldBeAsync(HttpStatusCode.BadRequest);
        await (await CompleteAsync(orderId, initialPayment: 200m)).ShouldBeAsync(HttpStatusCode.Conflict); // pays the gross
        Assert.Equal(OrderStatus.Draft, await OrderStatusAsync(orderId));

        await (await CompleteAsync(orderId, initialPayment: 150m)).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(150m, await PaidSumAsync(orderId));
        Assert.Equal(8, await StockAsync(product));
    }

    [Fact]
    public async Task Discount_larger_than_the_subtotal_is_rejected_at_creation()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var resp = await Admin.PostAsJsonAsync("/api/orders",
            new { customerId = (int?)null, discountAmount = 500m, items = new[] { Item(product, 1) } });
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Customer_sales_may_be_credit_or_partially_paid()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();

        var (credit, _) = await CreateOrderAsync(customer, 0m, Item(product, 2)); // 200, no payment
        await (await CompleteAsync(credit)).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(0m, await PaidSumAsync(credit));

        var (partial, _) = await CreateOrderAsync(customer, 0m, Item(product, 2)); // 200, pay 80
        await (await CompleteAsync(partial, initialPayment: 80m)).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(80m, await PaidSumAsync(partial));

        Assert.Equal(6, await StockAsync(product));
    }

    [Fact]
    public async Task Zero_initial_payment_is_rejected()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var (orderId, _) = await CreateOrderAsync(null, 0m, Item(product, 1));

        await (await CompleteAsync(orderId, initialPayment: 0m)).ShouldBeAsync(HttpStatusCode.BadRequest);
        await AssertNothingChangedAsync(orderId, product, 10);
    }

    [Fact]
    public async Task Cashier_can_run_a_walk_in_sale_but_warehouse_cannot()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var (orderId, total) = await CreateOrderAsync(null, 0m, Item(product, 1));

        await (await CompleteAsync(orderId, total, Warehouse)).ShouldBeAsync(HttpStatusCode.Forbidden);
        await (await CompleteAsync(orderId, total, Anonymous)).ShouldBeAsync(HttpStatusCode.Unauthorized);
        Assert.Equal(OrderStatus.Draft, await OrderStatusAsync(orderId));

        await (await CompleteAsync(orderId, total, Cashier)).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(9, await StockAsync(product));
    }

    // ---- Historical walk-in orders (created before the rule) and the Payments API ----

    [Fact]
    public async Task Payments_API_cannot_partially_pay_a_historical_walk_in_order()
    {
        var (orderId, _) = await SeedHistoricalWalkInOrderAsync(total: 200m, paid: 0m);

        await (await PayAsync(orderId, 100m)).ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal(0, await PaymentCountAsync());

        var full = await PayAsync(orderId, 200m);
        await full.ShouldBeAsync(HttpStatusCode.Created);
        Assert.Equal("Paid", (await full.ReadJsonAsync()).Str("paymentState"));
    }

    [Fact]
    public async Task Voiding_the_payment_of_a_paid_walk_in_order_is_blocked()
    {
        var (orderId, paymentId) = await SeedHistoricalWalkInOrderAsync(total: 200m, paid: 200m);

        var resp = await Admin.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "should be blocked" });
        await resp.ShouldBeAsync(HttpStatusCode.Conflict);

        Assert.Equal(200m, await PaidSumAsync(orderId)); // payment untouched, no new walk-in debt created
    }
}
