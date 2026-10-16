using System.Net;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Idempotency;
using ToolShop.Infrastructure.Sales;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// F1b - idempotency of Complete (sale + optional initial payment) and Return. One Idempotency-Key = one user intention:
/// the same request sent again (double click, retry after a lost answer, two devices racing) returns the first result and writes
/// NOTHING (no second payment, return, refund, stock movement or audit entry); a different request under the same key is a
/// conflict; a rejected request does not use up its key; a genuinely new operation (e.g. a second partial return) uses a new key.
/// </summary>
[Collection("db")]
public class IdempotentOrderOperationsTests(ApiFactory factory) : TestBase(factory)
{
    private static bool IsReplay(HttpResponseMessage r) =>
        r.Headers.TryGetValues("Idempotent-Replay", out var v) && v.Contains("true");

    private Task<int> RecordCountAsync() => Factory.WithDbAsync(db => db.IdempotencyRecords.CountAsync());
    private Task<int> AuditCountAsync(string action) => Factory.WithDbAsync(db => db.AuditLogs.CountAsync(a => a.Action == action));
    private Task<int> ReturnCountAsync(int orderId) => Factory.WithDbAsync(db => db.OrderReturns.CountAsync(r => r.OrderId == orderId));
    private Task<int> SaleMovementsAsync(int productId) => Factory.WithDbAsync(db =>
        db.InventoryMovements.CountAsync(m => m.ProductId == productId && m.MovementType == InventoryMovementType.Sale));
    private Task<int> SaleReturnMovementsAsync(int productId) => Factory.WithDbAsync(db =>
        db.InventoryMovements.CountAsync(m => m.ProductId == productId && m.MovementType == InventoryMovementType.SaleReturn));
    private Task<List<int>> ItemIdsAsync(int orderId) => Factory.WithDbAsync(db =>
        db.OrderItems.Where(i => i.OrderId == orderId).OrderBy(i => i.Id).Select(i => i.Id).ToListAsync());

    private static object Line(int orderItemId, int quantity, int damaged = 0) =>
        new { orderItemId, quantity, damagedQuantity = damaged };

    private Task<HttpResponseMessage> ReturnAsync(int orderId, object? items, string? key, string reason = "Customer returned goods",
        HttpClient? client = null) =>
        PostWithKeyAsync(client ?? Admin, $"/api/orders/{orderId}/return", new { reason, items }, key);

    /// <summary>A customer DRAFT of 5 x 100 (stock 20).</summary>
    private async Task<(int OrderId, int ProductId, int CustomerId)> DraftAsync(int quantity = 5, int stock = 20)
    {
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: stock);
        var customer = await SeedCustomerAsync();
        var (id, _) = await CreateOrderAsync(customer, 0m, Item(product, quantity));
        return (id, product, customer);
    }

    /// <summary>A COMPLETED customer sale of 5 x 100 paid in full. Returns (order, its single line, product).</summary>
    private async Task<(int OrderId, int ItemId, int ProductId)> SoldAsync()
    {
        var (id, product, _) = await DraftAsync();
        await (await CompleteAsync(id, 500m)).ShouldBeAsync(HttpStatusCode.OK);
        return (id, (await ItemIdsAsync(id))[0], product);
    }

    // ======================= Complete =======================

    [Fact]
    public async Task Complete_same_key_twice_sells_once_and_replays_the_result()
    {
        var (id, product, _) = await DraftAsync();
        var key = NewKey();

        var first = await CompleteAsync(id, key: key);
        await first.ShouldBeAsync(HttpStatusCode.OK);
        Assert.False(IsReplay(first));

        var second = await CompleteAsync(id, key: key);
        await second.ShouldBeAsync(HttpStatusCode.OK);
        Assert.True(IsReplay(second), "The second answer must be marked as a replay.");

        var a = await first.ReadJsonAsync();
        var b = await second.ReadJsonAsync();
        Assert.Equal("Completed", b.Str("status"));
        Assert.Equal(a.Str("orderNumber"), b.Str("orderNumber"));
        Assert.Equal(a.Dec("totalAmount"), b.Dec("totalAmount"));

        Assert.Equal(15, await StockAsync(product));
        Assert.Equal(1, await SaleMovementsAsync(product));
        Assert.Equal(1, await AuditCountAsync("Order.Complete"));
        Assert.Equal(1, await RecordCountAsync());
    }

    [Fact]
    public async Task Complete_with_initial_payment_replayed_records_one_payment_and_one_audit()
    {
        var (id, product, _) = await DraftAsync();
        var key = NewKey();

        await (await CompleteAsync(id, 200m, key: key)).ShouldBeAsync(HttpStatusCode.OK);
        var replay = await CompleteAsync(id, 200m, key: key);
        await replay.ShouldBeAsync(HttpStatusCode.OK);
        Assert.True(IsReplay(replay));

        Assert.Equal(1, await PaymentCountAsync());
        Assert.Equal(200m, await PaidSumAsync(id));
        Assert.Equal(1, await AuditCountAsync("Payment.Create"));
        Assert.Equal(15, await StockAsync(product));
    }

    [Fact]
    public async Task Ten_parallel_completes_with_the_same_key_make_exactly_one_sale()
    {
        var (id, product, _) = await DraftAsync();
        var key = NewKey();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => CompleteAsync(id, 200m, key: key)));

        foreach (var r in results)
        {
            Assert.True((int)r.StatusCode < 500, "HTTP 5xx. Server error log:\n" + ErrorCaptureProvider.Dump());
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);   // winners and replays alike: never a 409 for the same intention
        }

        Assert.Equal(1, results.Count(r => !IsReplay(r)));    // exactly one real completion
        Assert.Equal(15, await StockAsync(product));          // stock left once
        Assert.Equal(1, await SaleMovementsAsync(product));
        Assert.Equal(1, await PaymentCountAsync());           // one initial payment
        Assert.Equal(200m, await PaidSumAsync(id));
        Assert.Equal(1, await AuditCountAsync("Order.Complete"));
        Assert.Equal(1, await RecordCountAsync());
        Assert.Equal(OrderStatus.Completed, await OrderStatusAsync(id));
    }

    [Fact]
    public async Task Complete_same_key_with_a_different_payment_is_a_conflict_and_changes_nothing()
    {
        var (id, product, _) = await DraftAsync();
        var key = NewKey();
        await (await CompleteAsync(id, 100m, key: key)).ShouldBeAsync(HttpStatusCode.OK);

        var other = await CompleteAsync(id, 150m, key: key);
        await other.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await other.ReadJsonAsync()).Str("code"));

        Assert.Equal(1, await PaymentCountAsync());
        Assert.Equal(100m, await PaidSumAsync(id));
        Assert.Equal(15, await StockAsync(product));
    }

    [Fact]
    public async Task Complete_same_key_for_another_order_is_a_conflict()
    {
        var (first, _, _) = await DraftAsync();
        var (second, secondProduct, _) = await DraftAsync();
        var key = NewKey();
        await (await CompleteAsync(first, key: key)).ShouldBeAsync(HttpStatusCode.OK);

        await (await CompleteAsync(second, key: key)).ShouldBeAsync(HttpStatusCode.Conflict);

        Assert.Equal(OrderStatus.Draft, await OrderStatusAsync(second));
        Assert.Equal(20, await StockAsync(secondProduct));
    }

    [Fact]
    public async Task Rejected_complete_does_not_use_up_its_key()
    {
        // A walk-in sale must be paid in full: the first try (no payment) is refused ...
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var (id, _) = await CreateOrderAsync(null, 0m, Item(product, 2));
        var key = NewKey();

        var refused = await CompleteAsync(id, key: key);
        await refused.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("WALKIN_FULL_PAYMENT_REQUIRED", (await refused.ReadJsonAsync()).Str("code"));
        Assert.Equal(0, await RecordCountAsync());            // the claim rolled back with the rejected sale
        Assert.Equal(OrderStatus.Draft, await OrderStatusAsync(id));

        // ... and the SAME key is still free for the corrected request (it is a different payload, but nothing was stored).
        var fixedUp = await CompleteAsync(id, 200m, key: key);
        await fixedUp.ShouldBeAsync(HttpStatusCode.OK);
        Assert.False(IsReplay(fixedUp));
        Assert.Equal(8, await StockAsync(product));
    }

    [Fact]
    public async Task Completing_an_already_completed_order_with_a_NEW_key_is_still_refused()
    {
        var (id, product, _) = await DraftAsync();
        await (await CompleteAsync(id)).ShouldBeAsync(HttpStatusCode.OK);

        await (await CompleteAsync(id)).ShouldBeAsync(HttpStatusCode.Conflict);   // new intention -> the business rule decides

        Assert.Equal(15, await StockAsync(product));
        Assert.Equal(1, await SaleMovementsAsync(product));
    }

    [Fact]
    public async Task Complete_requires_a_valid_key_and_changes_nothing_without_one()
    {
        var (id, product, _) = await DraftAsync();
        var url = $"/api/orders/{id}/complete";

        var missing = await PostWithKeyAsync(Admin, url, new { }, null);
        await missing.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", (await missing.ReadJsonAsync()).Str("code"));

        foreach (var bad in new[] { "short", "has spaces in it 1234567890", new string('a', 65) })
        {
            var resp = await PostWithKeyAsync(Admin, url, new { }, bad);
            await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
            Assert.Equal("IDEMPOTENCY_KEY_INVALID", (await resp.ReadJsonAsync()).Str("code"));
        }

        Assert.Equal(OrderStatus.Draft, await OrderStatusAsync(id));
        Assert.Equal(20, await StockAsync(product));
        Assert.Equal(0, await RecordCountAsync());
    }

    [Fact]
    public async Task Complete_keys_are_private_to_the_user_who_sent_them()
    {
        var (a, _, _) = await DraftAsync();
        var (b, _, _) = await DraftAsync();
        var key = NewKey();

        var byAdmin = await CompleteAsync(a, key: key);
        var byManager = await CompleteAsync(b, client: Manager, key: key);

        await byAdmin.ShouldBeAsync(HttpStatusCode.OK);
        await byManager.ShouldBeAsync(HttpStatusCode.OK);
        Assert.False(IsReplay(byManager), "Another user's key must not be treated as a replay.");
        Assert.Equal(2, await RecordCountAsync());
    }

    [Fact]
    public async Task An_expired_complete_key_is_forgotten_and_can_be_used_again()
    {
        var (a, _, _) = await DraftAsync();
        var (b, bProduct, _) = await DraftAsync();
        var key = NewKey();
        await (await CompleteAsync(a, key: key)).ShouldBeAsync(HttpStatusCode.OK);

        await Factory.WithDbAsync(db => db.IdempotencyRecords.ExecuteUpdateAsync(s =>
            s.SetProperty(r => r.ExpiresAt, DateTime.UtcNow.AddDays(-1))));

        var again = await CompleteAsync(b, key: key);
        await again.ShouldBeAsync(HttpStatusCode.OK);
        Assert.False(IsReplay(again));
        Assert.Equal(15, await StockAsync(bProduct));
        Assert.Equal(1, await RecordCountAsync());            // the old row was replaced, not duplicated
    }

    [Fact]
    public async Task The_stored_complete_record_points_at_the_order_and_carries_the_request_fingerprint()
    {
        var (id, _, _) = await DraftAsync();
        var key = NewKey();
        await (await CompleteAsync(id, 125.5m, key: key)).ShouldBeAsync(HttpStatusCode.OK);

        var row = await Factory.WithDbAsync(db => db.IdempotencyRecords.SingleAsync());
        Assert.Equal("order.complete", row.Scope);
        Assert.Equal(key, row.Key);
        Assert.Equal("Order", row.ResultEntityType);
        Assert.Equal(id, row.ResultEntityId);
        Assert.Equal(OrderRequestHash.ForComplete(id, new InitialPaymentInput(125.5m, PaymentMethod.Cash, null, null)), row.RequestHash);
        Assert.InRange((row.ExpiresAt - row.CreatedAt).TotalDays, 29.9, 30.1);
    }

    // ======================= Return =======================

    [Fact]
    public async Task Return_same_key_twice_records_one_return_one_refund_and_one_restock()
    {
        var (id, itemId, product) = await SoldAsync();
        var key = NewKey();

        var first = await ReturnAsync(id, new[] { Line(itemId, 2) }, key);
        await first.ShouldBeAsync(HttpStatusCode.OK);
        Assert.False(IsReplay(first));

        var second = await ReturnAsync(id, new[] { Line(itemId, 2) }, key);
        await second.ShouldBeAsync(HttpStatusCode.OK);
        Assert.True(IsReplay(second), "The second answer must be marked as a replay.");

        var a = await first.ReadJsonAsync();
        var b = await second.ReadJsonAsync();
        Assert.Equal("PartiallyReturned", b.Str("status"));
        Assert.Equal(a.Dec("returnedValue"), b.Dec("returnedValue"));
        Assert.Equal(200m, b.Dec("refundedAmount"));          // refunded ONCE, not 400

        Assert.Equal(1, await ReturnCountAsync(id));
        Assert.Equal(17, await StockAsync(product));          // restocked once
        Assert.Equal(1, await SaleReturnMovementsAsync(product));
        Assert.Equal(1, await AuditCountAsync("Order.Return"));
    }

    [Fact]
    public async Task Ten_parallel_returns_with_the_same_key_make_exactly_one_return()
    {
        var (id, itemId, product) = await SoldAsync();
        var key = NewKey();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ReturnAsync(id, new[] { Line(itemId, 2) }, key)));

        foreach (var r in results)
        {
            Assert.True((int)r.StatusCode < 500, "HTTP 5xx. Server error log:\n" + ErrorCaptureProvider.Dump());
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        Assert.Equal(1, results.Count(r => !IsReplay(r)));
        Assert.Equal(1, await ReturnCountAsync(id));
        Assert.Equal(17, await StockAsync(product));
        Assert.Equal(1, await SaleReturnMovementsAsync(product));
        Assert.Equal(1, await AuditCountAsync("Order.Return"));

        var pay = await (await Admin.GetAsync($"/api/orders/{id}/payments")).ReadJsonAsync();
        Assert.Equal(200m, pay.Dec("refundedTotal"));         // 200 paid back once, never 2000
        Assert.Equal(OrderStatus.PartiallyReturned, await OrderStatusAsync(id));
    }

    [Fact]
    public async Task A_second_genuine_partial_return_needs_and_gets_a_new_key()
    {
        var (id, itemId, product) = await SoldAsync();
        var firstKey = NewKey();

        await (await ReturnAsync(id, new[] { Line(itemId, 2) }, firstKey)).ShouldBeAsync(HttpStatusCode.OK);
        var second = await ReturnAsync(id, new[] { Line(itemId, 3) }, NewKey());   // new intention -> new key
        await second.ShouldBeAsync(HttpStatusCode.OK);
        Assert.False(IsReplay(second));
        Assert.Equal("Returned", (await second.ReadJsonAsync()).Str("status"));

        // Re-sending the FIRST request still gives the first result, even though the order is fully returned by now.
        var again = await ReturnAsync(id, new[] { Line(itemId, 2) }, firstKey);
        await again.ShouldBeAsync(HttpStatusCode.OK);
        Assert.True(IsReplay(again));

        Assert.Equal(2, await ReturnCountAsync(id));
        Assert.Equal(20, await StockAsync(product));
        Assert.Equal(500m, (await (await Admin.GetAsync($"/api/orders/{id}/payments")).ReadJsonAsync()).Dec("refundedTotal"));
    }

    [Fact]
    public async Task Return_same_key_with_a_different_request_is_a_conflict_and_changes_nothing()
    {
        var (id, itemId, product) = await SoldAsync();
        var key = NewKey();
        await (await ReturnAsync(id, new[] { Line(itemId, 2) }, key)).ShouldBeAsync(HttpStatusCode.OK);

        foreach (var (items, reason) in new (object, string)[]
                 {
                     (new[] { Line(itemId, 3) }, "Customer returned goods"),   // other quantity
                     (new[] { Line(itemId, 2, damaged: 1) }, "Customer returned goods"),   // other damaged count
                     (new[] { Line(itemId, 2) }, "A different reason"),
                 })
        {
            var resp = await ReturnAsync(id, items, key, reason);
            await resp.ShouldBeAsync(HttpStatusCode.Conflict);
            Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await resp.ReadJsonAsync()).Str("code"));
        }

        Assert.Equal(1, await ReturnCountAsync(id));
        Assert.Equal(17, await StockAsync(product));
    }

    [Fact]
    public async Task Rejected_return_does_not_use_up_its_key()
    {
        var (id, itemId, product) = await SoldAsync();
        var key = NewKey();

        var tooMany = await ReturnAsync(id, new[] { Line(itemId, 99) }, key);
        await tooMany.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("QUANTITY_EXCEEDS_REMAINING", (await tooMany.ReadJsonAsync()).Str("code"));
        // The sale itself stored its own "order.complete" key; the rejected return must have stored NO "order.return" key.
        Assert.Equal(0, await Factory.WithDbAsync(db => db.IdempotencyRecords.CountAsync(r => r.Scope == "order.return")));

        var corrected = await ReturnAsync(id, new[] { Line(itemId, 2) }, key);
        await corrected.ShouldBeAsync(HttpStatusCode.OK);
        Assert.False(IsReplay(corrected));
        Assert.Equal(17, await StockAsync(product));
    }

    [Fact]
    public async Task Whole_order_return_replayed_after_the_order_is_fully_returned_gives_the_same_result()
    {
        var (id, _, product) = await SoldAsync();
        var key = NewKey();

        var first = await ReturnAsync(id, null, key);          // no lines = everything that is left
        await first.ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal("Returned", (await first.ReadJsonAsync()).Str("status"));

        var replay = await ReturnAsync(id, null, key);         // the order is Returned now, the key still replays
        await replay.ShouldBeAsync(HttpStatusCode.OK);
        Assert.True(IsReplay(replay));
        Assert.Equal("Returned", (await replay.ReadJsonAsync()).Str("status"));

        Assert.Equal(1, await ReturnCountAsync(id));
        Assert.Equal(20, await StockAsync(product));
        Assert.Equal(1, await SaleReturnMovementsAsync(product));

        // A NEW key on the finished order is a new intention and the business rule refuses it.
        await (await ReturnAsync(id, null, NewKey())).ShouldBeAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Return_replays_never_change_balances_stock_or_returns()
    {
        var (id, itemId, product) = await SoldAsync();
        var key = NewKey();
        await (await ReturnAsync(id, new[] { Line(itemId, 2) }, key)).ShouldBeAsync(HttpStatusCode.OK);

        async Task<(decimal Refunded, decimal Returned, decimal Outstanding, int Stock, int Movements, int Returns, int Audits)> SnapshotAsync()
        {
            var pay = await (await Admin.GetAsync($"/api/orders/{id}/payments")).ReadJsonAsync();
            return (pay.Dec("refundedTotal"), pay.Dec("returnedValue"), pay.Dec("outstandingBalance"),
                await StockAsync(product), await MovementCountAsync(), await ReturnCountAsync(id),
                await AuditCountAsync("Order.Return"));
        }

        var before = await SnapshotAsync();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(IsReplay(await ReturnAsync(id, new[] { Line(itemId, 2) }, key)));
        }

        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(1, before.Returns);
        Assert.Equal(1, before.Audits);
    }

    [Fact]
    public async Task Return_requires_a_valid_key_and_changes_nothing_without_one()
    {
        var (id, itemId, product) = await SoldAsync();

        var missing = await ReturnAsync(id, new[] { Line(itemId, 2) }, null);
        await missing.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", (await missing.ReadJsonAsync()).Str("code"));

        var invalid = await ReturnAsync(id, new[] { Line(itemId, 2) }, "too-short");
        await invalid.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("IDEMPOTENCY_KEY_INVALID", (await invalid.ReadJsonAsync()).Str("code"));

        Assert.Equal(0, await ReturnCountAsync(id));
        Assert.Equal(15, await StockAsync(product));
    }

    [Fact]
    public async Task Return_permission_is_still_enforced_with_a_valid_key()
    {
        var (id, itemId, product) = await SoldAsync();

        await (await ReturnAsync(id, new[] { Line(itemId, 2) }, NewKey(), client: Cashier)).ShouldBeAsync(HttpStatusCode.Forbidden);

        Assert.Equal(0, await ReturnCountAsync(id));
        Assert.Equal(15, await StockAsync(product));
        Assert.Equal(0, await Factory.WithDbAsync(db => db.IdempotencyRecords.CountAsync(r => r.Scope == "order.return")));
    }

    [Fact]
    public async Task Complete_and_Return_keys_are_independent_even_when_the_text_is_the_same()
    {
        var (id, product, _) = await DraftAsync();
        var key = NewKey();
        await (await CompleteAsync(id, 500m, key: key)).ShouldBeAsync(HttpStatusCode.OK);
        var itemId = (await ItemIdsAsync(id))[0];

        var ret = await ReturnAsync(id, new[] { Line(itemId, 1) }, key);   // same key text, other operation

        await ret.ShouldBeAsync(HttpStatusCode.OK);
        Assert.False(IsReplay(ret));
        Assert.Equal(16, await StockAsync(product));
        Assert.Equal(2, await RecordCountAsync());
    }

    [Fact]
    public async Task The_stored_return_record_points_at_the_return_and_carries_the_request_fingerprint()
    {
        var (id, itemId, _) = await SoldAsync();
        var key = NewKey();
        await (await ReturnAsync(id, new[] { Line(itemId, 2) }, key)).ShouldBeAsync(HttpStatusCode.OK);

        var ret = await Factory.WithDbAsync(db => db.OrderReturns.SingleAsync(r => r.OrderId == id));
        var row = await Factory.WithDbAsync(db => db.IdempotencyRecords.SingleAsync(r => r.Scope == "order.return"));

        Assert.Equal(key, row.Key);
        Assert.Equal("OrderReturn", row.ResultEntityType);
        Assert.Equal(ret.Id, row.ResultEntityId);
        var expected = OrderRequestHash.ForReturn(id,
            new ReturnInput("Customer returned goods", new List<ReturnLineInput> { new(itemId, 2, 0) }, null, null));
        Assert.Equal(expected, row.RequestHash);
    }

    // ======================= fingerprints (pure) =======================

    [Fact]
    public void Complete_fingerprint_ignores_harmless_differences_and_sees_real_ones()
    {
        var none = OrderRequestHash.ForComplete(7, null);
        var cash = new InitialPaymentInput(100m, PaymentMethod.Cash, null, null);

        Assert.Equal(none, OrderRequestHash.ForComplete(7, null));
        Assert.Equal(OrderRequestHash.ForComplete(7, cash), OrderRequestHash.ForComplete(7, cash with { Amount = 100.00m }));
        Assert.Equal(OrderRequestHash.ForComplete(7, cash), OrderRequestHash.ForComplete(7, cash with { ReferenceNumber = "  ", Notes = "" }));

        Assert.NotEqual(none, OrderRequestHash.ForComplete(7, cash));
        Assert.NotEqual(none, OrderRequestHash.ForComplete(8, null));
        Assert.NotEqual(OrderRequestHash.ForComplete(7, cash), OrderRequestHash.ForComplete(7, cash with { Amount = 100.01m }));
        Assert.NotEqual(OrderRequestHash.ForComplete(7, cash), OrderRequestHash.ForComplete(7, cash with { Method = PaymentMethod.Card }));
        Assert.NotEqual(OrderRequestHash.ForComplete(7, cash), OrderRequestHash.ForComplete(7, cash with { ReferenceNumber = "R1" }));
    }

    [Fact]
    public void Return_fingerprint_ignores_line_order_and_spaces_and_sees_real_differences()
    {
        ReturnInput Make(string reason = "Broken", List<ReturnLineInput>? lines = null,
            PaymentMethod? method = null, decimal? amount = null) =>
            new(reason, lines ?? new List<ReturnLineInput> { new(1, 2, 0), new(2, 1, 1) }, method, amount);

        var baseline = OrderRequestHash.ForReturn(5, Make());

        Assert.Equal(baseline, OrderRequestHash.ForReturn(5, Make("  Broken  ")));
        Assert.Equal(baseline, OrderRequestHash.ForReturn(5, Make(lines: new List<ReturnLineInput> { new(2, 1, 1), new(1, 2, 0) })));

        Assert.NotEqual(baseline, OrderRequestHash.ForReturn(6, Make()));
        Assert.NotEqual(baseline, OrderRequestHash.ForReturn(5, Make("Other")));
        Assert.NotEqual(baseline, OrderRequestHash.ForReturn(5, Make(lines: new List<ReturnLineInput> { new(1, 3, 0), new(2, 1, 1) })));
        Assert.NotEqual(baseline, OrderRequestHash.ForReturn(5, Make(lines: new List<ReturnLineInput> { new(1, 2, 1), new(2, 1, 1) })));
        Assert.NotEqual(baseline, OrderRequestHash.ForReturn(5, Make(method: PaymentMethod.Card)));
        Assert.NotEqual(baseline, OrderRequestHash.ForReturn(5, Make(amount: 0m)));       // explicit 0 differs from "automatic"

        // "everything that is left" (no lines) is its own request, whether Lines is null or empty.
        var all = OrderRequestHash.ForReturn(5, new ReturnInput("Broken", null, null, null));
        Assert.Equal(all, OrderRequestHash.ForReturn(5, new ReturnInput("Broken", new List<ReturnLineInput>(), null, null)));
        Assert.NotEqual(baseline, all);
    }
}
