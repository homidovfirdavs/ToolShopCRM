using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>TASK C2 - partial / full returns recorded in the immutable returns ledger.</summary>
[Collection("db")]
public class PartialReturnTests(ApiFactory factory) : TestBase(factory)
{
    /// <summary>Every call is a NEW return operation with its own Idempotency-Key, unless a key is passed to resend it.</summary>
    private Task<HttpResponseMessage> ReturnAsync(int orderId, object? items = null, string? refundMethod = null,
        decimal? refundAmount = null, HttpClient? client = null, string? key = null) =>
        PostWithKeyAsync(client ?? Admin, $"/api/orders/{orderId}/return",
            new { reason = "Customer returned goods", items, refundMethod, refundAmount }, key ?? NewKey());

    private static object Line(int orderItemId, int quantity, int damaged = 0) =>
        new { orderItemId, quantity, damagedQuantity = damaged };

    private Task<List<int>> ItemIdsAsync(int orderId) => Factory.WithDbAsync(db =>
        db.OrderItems.Where(i => i.OrderId == orderId).OrderBy(i => i.Id).Select(i => i.Id).ToListAsync());

    private Task<List<OrderReturn>> ReturnsAsync(int orderId) => Factory.WithDbAsync(db =>
        db.OrderReturns.Include(r => r.Items).Where(r => r.OrderId == orderId).OrderBy(r => r.Id).ToListAsync());

    /// <summary>price 100, cost 60, stock 20 -> sold 5 to a customer; paid = paid (null = in full).</summary>
    private async Task<(int OrderId, int ItemId, int ProductId, decimal Total)> SaleAsync(decimal? paid = null, decimal orderDiscount = 0m,
        int qty = 5, decimal price = 100m)
    {
        var product = await SeedProductAsync(salePrice: price, purchasePrice: 60m, stock: 20);
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, orderDiscount, Item(product, qty));
        await (await CompleteAsync(id, paid ?? total)).ShouldBeAsync(HttpStatusCode.OK);
        var itemId = (await ItemIdsAsync(id))[0];
        return (id, itemId, product, total);
    }

    [Fact]
    public async Task Partial_return_restocks_only_returned_units_and_keeps_payments()
    {
        var (id, itemId, product, total) = await SaleAsync();
        Assert.Equal(15, await StockAsync(product));

        var resp = await ReturnAsync(id, new[] { Line(itemId, 2) });
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        var json = await resp.ReadJsonAsync();

        Assert.Equal("PartiallyReturned", json.Str("status"));
        Assert.Equal(200m, json.Dec("returnedValue"));
        Assert.Equal(200m, json.Dec("refundedAmount"));
        Assert.Equal(17, await StockAsync(product));
        Assert.Equal(total, await PaidSumAsync(id));                       // the payment is untouched

        var ret = Assert.Single(await ReturnsAsync(id));
        Assert.StartsWith("RET-", ret.ReturnNumber);
        Assert.Equal(200m, ret.GoodsValue);
        Assert.Equal(120m, ret.CostReversed);                              // 2 x 60 goes back out of COGS
        Assert.Equal(0m, ret.WriteOffCost);
        Assert.Equal(PaymentMethod.Cash, ret.RefundMethod);

        var pay = await (await Admin.GetAsync($"/api/orders/{id}/payments")).ReadJsonAsync();
        Assert.Equal(0m, pay.Dec("outstandingBalance"));
        Assert.Equal(200m, pay.Dec("refundedTotal"));
        Assert.Equal("Paid", pay.Str("paymentState"));
    }

    [Fact]
    public async Task Two_partial_returns_add_up_to_the_order_and_finish_it()
    {
        var (id, itemId, product, total) = await SaleAsync();
        await (await ReturnAsync(id, new[] { Line(itemId, 2) })).ShouldBeAsync(HttpStatusCode.OK);
        var second = await ReturnAsync(id, new[] { Line(itemId, 3) });
        await second.ShouldBeAsync(HttpStatusCode.OK);

        Assert.Equal("Returned", (await second.ReadJsonAsync()).Str("status"));
        Assert.Equal(20, await StockAsync(product));
        var rets = await ReturnsAsync(id);
        Assert.Equal(2, rets.Count);
        Assert.Equal(total, rets.Sum(r => r.GoodsValue));
        Assert.Equal(total, rets.Sum(r => r.RefundAmount));
        Assert.NotEqual(rets[0].ReturnNumber, rets[1].ReturnNumber);
        Assert.Equal(OrderStatus.Returned, await OrderStatusAsync(id));
    }

    [Fact]
    public async Task Order_discount_is_allocated_pro_rata_and_the_last_return_takes_the_remainder()
    {
        // 3 x 10 = 30, order discount 1 -> total 29 : thirds are 9.67 + 9.67 + 9.66
        var (id, itemId, _, total) = await SaleAsync(qty: 3, price: 10m, orderDiscount: 1m);
        Assert.Equal(29m, total);

        await (await ReturnAsync(id, new[] { Line(itemId, 1) })).ShouldBeAsync(HttpStatusCode.OK);
        await (await ReturnAsync(id, new[] { Line(itemId, 1) })).ShouldBeAsync(HttpStatusCode.OK);
        await (await ReturnAsync(id, new[] { Line(itemId, 1) })).ShouldBeAsync(HttpStatusCode.OK);

        var rets = await ReturnsAsync(id);
        Assert.Equal(new[] { 9.67m, 9.67m, 9.66m }, rets.Select(r => r.GoodsValue).ToArray());
        Assert.Equal(29m, rets.Sum(r => r.GoodsValue));
        Assert.Equal(29m, rets.Sum(r => r.RefundAmount));
    }

    [Fact]
    public async Task Damaged_units_do_not_go_back_to_stock_and_are_written_off_at_cost()
    {
        var (id, itemId, product, _) = await SaleAsync();
        var movementsBefore = await MovementCountAsync();

        await (await ReturnAsync(id, new[] { Line(itemId, 2, damaged: 1) })).ShouldBeAsync(HttpStatusCode.OK);

        Assert.Equal(16, await StockAsync(product));                       // only 1 sellable unit came back
        Assert.Equal(movementsBefore + 1, await MovementCountAsync());
        var restockMovement = await Factory.WithDbAsync(db =>
            db.InventoryMovements.SingleAsync(m => m.ReferenceId == id && m.MovementType == InventoryMovementType.SaleReturn));
        Assert.Equal(1, restockMovement.Quantity);

        var ret = Assert.Single(await ReturnsAsync(id));
        Assert.Equal(200m, ret.GoodsValue);                                // revenue is reversed for ALL returned units
        Assert.Equal(60m, ret.CostReversed);                               // only the restocked unit leaves COGS
        Assert.Equal(60m, ret.WriteOffCost);                               // the damaged unit stays in COGS (memo)
        var item = Assert.Single(ret.Items);
        Assert.Equal(1, item.RestockedQuantity);
        Assert.Equal(1, item.WrittenOffQuantity);
    }

    [Fact]
    public async Task Fully_damaged_return_creates_no_stock_movement()
    {
        var (id, itemId, product, _) = await SaleAsync();
        var movementsBefore = await MovementCountAsync();

        await (await ReturnAsync(id, new[] { Line(itemId, 5, damaged: 5) })).ShouldBeAsync(HttpStatusCode.OK);

        Assert.Equal(15, await StockAsync(product));
        Assert.Equal(movementsBefore, await MovementCountAsync());
        var ret = Assert.Single(await ReturnsAsync(id));
        Assert.Equal(0m, ret.CostReversed);                                // real zero: nothing restocked
        Assert.Equal(300m, ret.WriteOffCost);
    }

    [Fact]
    public async Task Unknown_cost_stays_null_and_is_never_turned_into_zero()
    {
        var (id, itemId, _, _) = await SaleAsync();
        await Factory.WithDbAsync(db => db.OrderItems.Where(i => i.Id == itemId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.CostAtSale, (decimal?)null)));

        await (await ReturnAsync(id, new[] { Line(itemId, 2, damaged: 1) })).ShouldBeAsync(HttpStatusCode.OK);

        var ret = Assert.Single(await ReturnsAsync(id));
        Assert.Null(ret.CostReversed);
        Assert.Null(ret.WriteOffCost);
        Assert.Null(Assert.Single(ret.Items).CostAtSaleSnapshot);
    }

    [Fact]
    public async Task Credit_sale_return_reduces_the_debt_and_refunds_nothing_when_not_overpaid()
    {
        var (id, itemId, _, _) = await SaleAsync(paid: 200m);              // total 500, paid 200, debt 300

        var resp = await ReturnAsync(id, new[] { Line(itemId, 2) });       // worth 200 -> debt 100, nothing overpaid
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(0m, (await resp.ReadJsonAsync()).Dec("refundedAmount"));

        var ret = Assert.Single(await ReturnsAsync(id));
        Assert.Equal(0m, ret.RefundAmount);
        Assert.Null(ret.RefundMethod);

        var pay = await (await Admin.GetAsync($"/api/orders/{id}/payments")).ReadJsonAsync();
        Assert.Equal(100m, pay.Dec("outstandingBalance"));
    }

    [Fact]
    public async Task Partially_returned_order_accepts_payments_up_to_its_reduced_debt()
    {
        var (id, itemId, _, _) = await SaleAsync(paid: 200m);
        await (await ReturnAsync(id, new[] { Line(itemId, 2) })).ShouldBeAsync(HttpStatusCode.OK);   // debt 100

        var tooMuch = await PayAsync(id, 101m);
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);         // the debt is 100, not 300

        await (await PayAsync(id, 100m)).ShouldBeAsync(HttpStatusCode.Created);
        var pay = await (await Admin.GetAsync($"/api/orders/{id}/payments")).ReadJsonAsync();
        Assert.Equal(0m, pay.Dec("outstandingBalance"));
    }

    [Fact]
    public async Task Refund_cannot_exceed_what_the_customer_overpaid_and_can_be_smaller()
    {
        var (id, itemId, _, _) = await SaleAsync();                        // fully paid 500

        var over = await ReturnAsync(id, new[] { Line(itemId, 2) }, refundAmount: 300m);
        await over.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("REFUND_EXCEEDS_LIMIT", (await over.ReadJsonAsync()).Str("code"));
        Assert.Empty(await ReturnsAsync(id));                              // nothing was saved

        var ok = await ReturnAsync(id, new[] { Line(itemId, 2) }, refundMethod: "Card", refundAmount: 50m);
        await ok.ShouldBeAsync(HttpStatusCode.OK);
        var ret = Assert.Single(await ReturnsAsync(id));
        Assert.Equal(50m, ret.RefundAmount);
        Assert.Equal(PaymentMethod.Card, ret.RefundMethod);
    }

    [Theory]
    [InlineData("qty", "QUANTITY_EXCEEDS_REMAINING")]
    [InlineData("dup", "DUPLICATE_LINE")]
    [InlineData("foreign", "INVALID_ITEM")]
    [InlineData("damaged", "INVALID_DAMAGED_QUANTITY")]
    public async Task Invalid_return_requests_are_rejected_and_change_nothing(string kind, string code)
    {
        var (id, itemId, product, _) = await SaleAsync();
        var (otherId, _, _, _) = await SaleAsync();
        var otherItem = (await ItemIdsAsync(otherId))[0];
        var movements = await MovementCountAsync();

        object items = kind switch
        {
            "qty" => new[] { Line(itemId, 6) },
            "dup" => new[] { Line(itemId, 1), Line(itemId, 1) },
            "foreign" => new[] { Line(otherItem, 1) },
            _ => new[] { Line(itemId, 2, damaged: 3) }
        };

        var resp = await ReturnAsync(id, items);
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal(code, (await resp.ReadJsonAsync()).Str("code"));
        Assert.Equal(movements, await MovementCountAsync());
        Assert.Empty(await ReturnsAsync(id));
        Assert.Equal(OrderStatus.Completed, await OrderStatusAsync(id));
        Assert.Equal(15, await StockAsync(product));
    }

    [Fact]
    public async Task Cannot_return_more_than_what_is_left_after_an_earlier_return()
    {
        var (id, itemId, _, _) = await SaleAsync();
        await (await ReturnAsync(id, new[] { Line(itemId, 4) })).ShouldBeAsync(HttpStatusCode.OK);

        var resp = await ReturnAsync(id, new[] { Line(itemId, 2) });       // only 1 unit left
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("QUANTITY_EXCEEDS_REMAINING", (await resp.ReadJsonAsync()).Str("code"));
    }

    [Fact]
    public async Task Voiding_a_payment_below_the_refunded_amount_is_rejected()
    {
        var (id, itemId, _, _) = await SaleAsync();
        await (await ReturnAsync(id, new[] { Line(itemId, 2) })).ShouldBeAsync(HttpStatusCode.OK);   // 200 refunded
        var paymentId = await Factory.WithDbAsync(db => db.Payments.Where(p => p.OrderId == id).Select(p => p.Id).SingleAsync());

        var resp = await Admin.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "mistake" });
        await resp.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("VOID_BELOW_REFUNDED", (await resp.ReadJsonAsync()).Str("code"));
        Assert.Equal(500m, await PaidSumAsync(id));
    }

    [Fact]
    public async Task Return_is_dated_now_and_the_stock_movement_has_the_same_moment()
    {
        var (id, itemId, _, _) = await SaleAsync();
        var before = DateTime.UtcNow;
        await (await ReturnAsync(id, new[] { Line(itemId, 1) })).ShouldBeAsync(HttpStatusCode.OK);
        var after = DateTime.UtcNow;

        var ret = Assert.Single(await ReturnsAsync(id));
        Assert.InRange(ret.ReturnedAt, before.AddSeconds(-1), after.AddSeconds(1));
        var movement = await Factory.WithDbAsync(db =>
            db.InventoryMovements.SingleAsync(m => m.ReferenceId == id && m.MovementType == InventoryMovementType.SaleReturn));
        Assert.Equal(ret.ReturnedAt, movement.CreatedAt);
        Assert.Equal(Factory.AdminUserId, ret.ReturnedByUserId);
    }

    [Fact]
    public async Task Parallel_returns_of_the_same_units_succeed_only_once()
    {
        var (id, itemId, product, _) = await SaleAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => ReturnAsync(id, new[] { Line(itemId, 5) })));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest));
        Assert.Single(await ReturnsAsync(id));
        Assert.Equal(20, await StockAsync(product));                       // restocked exactly once
    }
}
