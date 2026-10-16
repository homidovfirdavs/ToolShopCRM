using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Infrastructure.Payments;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// F1 - payment idempotency. One Idempotency-Key = one user intention: the same request sent again (double click, retry after a
/// timeout, two devices racing) returns the first payment and writes nothing; a different request under the same key is a
/// conflict; a rejected request does not use up its key. Orders / debts / finance are never touched by a replay.
/// </summary>
[Collection("db")]
public class IdempotentPaymentTests(ApiFactory factory) : TestBase(factory)
{
    private static bool IsReplay(HttpResponseMessage r) =>
        r.Headers.TryGetValues("Idempotent-Replay", out var v) && v.Contains("true");

    /// <summary>A customer order of 800 (8 x 100) that is completed on credit: outstanding 800.</summary>
    private async Task<(int OrderId, int CustomerId)> CreditOrderAsync(int quantity = 8)
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 50);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateCompletedCreditOrderAsync(customer, product, quantity);
        return (orderId, customer);
    }

    // Only the payment keys: completing the sales that these tests set up stores its own "order.complete" keys (F1b).
    private Task<int> RecordCountAsync() => Factory.WithDbAsync(db => db.IdempotencyRecords.CountAsync(r => r.Scope == "payment.create"));

    // ---------------- same request, same key ----------------

    [Fact]
    public async Task Same_key_and_same_request_twice_records_one_payment_and_replays_it()
    {
        var (orderId, _) = await CreditOrderAsync();
        var key = NewKey();

        var first = await PayAsync(orderId, 300m, key: key);
        await first.ShouldBeAsync(HttpStatusCode.Created);
        Assert.False(IsReplay(first));

        var second = await PayAsync(orderId, 300m, key: key);
        await second.ShouldBeAsync(HttpStatusCode.Created);
        Assert.True(IsReplay(second), "The second answer must be marked as a replay.");

        var a = await first.ReadJsonAsync();
        var b = await second.ReadJsonAsync();
        Assert.Equal(a.Int("id"), b.Int("id"));
        Assert.Equal(a.Str("paymentNumber"), b.Str("paymentNumber"));

        Assert.Equal(1, await PaymentCountAsync());
        Assert.Equal(300m, await PaidSumAsync(orderId));
        Assert.Equal(1, await RecordCountAsync());
    }

    [Fact]
    public async Task Ten_parallel_requests_with_the_same_key_create_exactly_one_payment()
    {
        var (orderId, _) = await CreditOrderAsync();
        var key = NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PayAsync(orderId, 100m, key: key)));

        // No 5xx, no 409: every request is told about THE payment.
        foreach (var r in responses) await r.ShouldBeAsync(HttpStatusCode.Created);

        var ids = new HashSet<int>();
        foreach (var r in responses) ids.Add((await r.ReadJsonAsync()).Int("id"));
        Assert.Single(ids);

        Assert.Equal(1, responses.Count(r => !IsReplay(r)));   // exactly one request created it
        Assert.Equal(1, await PaymentCountAsync());
        Assert.Equal(100m, await PaidSumAsync(orderId));
        Assert.Equal(1, await RecordCountAsync());
    }

    [Fact]
    public async Task A_replay_after_the_original_was_voided_returns_the_voided_payment_and_creates_nothing()
    {
        var (orderId, _) = await CreditOrderAsync();
        var key = NewKey();

        var first = await PayAsync(orderId, 300m, key: key);
        await first.ShouldBeAsync(HttpStatusCode.Created);
        var paymentId = (await first.ReadJsonAsync()).Int("id");

        await (await Admin.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "entered by mistake" }))
            .ShouldBeAsync(HttpStatusCode.OK);

        var replay = await PayAsync(orderId, 300m, key: key);
        await replay.ShouldBeAsync(HttpStatusCode.Created);
        Assert.True(IsReplay(replay));
        Assert.Equal(paymentId, (await replay.ReadJsonAsync()).Int("id"));

        Assert.Equal(1, await PaymentCountAsync());     // not re-created
        Assert.Equal(0m, await PaidSumAsync(orderId));  // the void still stands
        var status = await Factory.WithDbAsync(async db => (await db.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId)).Status);
        Assert.Equal(PaymentStatus.Voided, status);
    }

    // ---------------- same key, different request ----------------

    [Theory]
    [InlineData(250, "Cash")]   // other amount
    [InlineData(300, "Card")]   // other method
    public async Task The_same_key_with_a_different_request_is_a_conflict_and_writes_nothing(int amount, string method)
    {
        var (orderId, _) = await CreditOrderAsync();
        var key = NewKey();
        await (await PayAsync(orderId, 300m, key: key)).ShouldBeAsync(HttpStatusCode.Created);

        var other = await PayAsync(orderId, amount, key: key, method: method);
        await other.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await other.ReadJsonAsync()).Str("code"));

        Assert.Equal(1, await PaymentCountAsync());
        Assert.Equal(300m, await PaidSumAsync(orderId));
    }

    [Fact]
    public async Task The_same_key_on_another_order_is_a_conflict()
    {
        var (orderA, _) = await CreditOrderAsync();
        var (orderB, _) = await CreditOrderAsync();
        var key = NewKey();
        await (await PayAsync(orderA, 100m, key: key)).ShouldBeAsync(HttpStatusCode.Created);

        await (await PayAsync(orderB, 100m, key: key)).ShouldBeAsync(HttpStatusCode.Conflict);

        Assert.Equal(1, await PaymentCountAsync());
        Assert.Equal(0m, await PaidSumAsync(orderB));
    }

    // ---------------- keys are per intention and per user ----------------

    [Fact]
    public async Task A_new_key_with_the_same_request_is_a_new_payment()
    {
        var (orderId, _) = await CreditOrderAsync();

        await (await PayAsync(orderId, 300m)).ShouldBeAsync(HttpStatusCode.Created);
        var second = await PayAsync(orderId, 300m);
        await second.ShouldBeAsync(HttpStatusCode.Created);
        Assert.False(IsReplay(second));

        Assert.Equal(2, await PaymentCountAsync());
        Assert.Equal(600m, await PaidSumAsync(orderId));
        Assert.Equal(2, await RecordCountAsync());
    }

    [Fact]
    public async Task Keys_are_private_to_the_user_who_sent_them()
    {
        var (orderId, _) = await CreditOrderAsync();
        var key = NewKey();

        var byAdmin = await PayAsync(orderId, 100m, Admin, key);
        await byAdmin.ShouldBeAsync(HttpStatusCode.Created);

        var byManager = await PayAsync(orderId, 100m, Manager, key);
        await byManager.ShouldBeAsync(HttpStatusCode.Created);
        Assert.False(IsReplay(byManager), "Another user's key must not be replayed to this user.");
        Assert.NotEqual((await byAdmin.ReadJsonAsync()).Int("id"), (await byManager.ReadJsonAsync()).Int("id"));

        Assert.Equal(2, await PaymentCountAsync());
    }

    // ---------------- failures do not burn the key ----------------

    [Fact]
    public async Task A_rejected_request_does_not_use_up_its_key()
    {
        var (orderId, _) = await CreditOrderAsync(quantity: 2); // owes 200
        var key = NewKey();

        await (await PayAsync(orderId, 300m, key: key)).ShouldBeAsync(HttpStatusCode.Conflict);  // overpayment
        Assert.Equal(0, await PaymentCountAsync());
        Assert.Equal(0, await RecordCountAsync());

        // The cashier corrects the amount and sends it again under the same key.
        var fixedUp = await PayAsync(orderId, 200m, key: key);
        await fixedUp.ShouldBeAsync(HttpStatusCode.Created);
        Assert.False(IsReplay(fixedUp));
        Assert.Equal(200m, await PaidSumAsync(orderId));
    }

    [Fact]
    public async Task A_payment_for_an_unknown_order_does_not_use_up_its_key()
    {
        var key = NewKey();
        await (await PayAsync(999_999, 10m, key: key)).ShouldBeAsync(HttpStatusCode.NotFound);
        Assert.Equal(0, await RecordCountAsync());
    }

    // ---------------- header rules ----------------

    [Fact]
    public async Task The_header_is_required()
    {
        var (orderId, _) = await CreditOrderAsync();

        var resp = await SendPaymentAsync(Admin, new { orderId, amount = 100m, method = "Cash" }, key: null);
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", (await resp.ReadJsonAsync()).Str("code"));
        Assert.Equal(0, await PaymentCountAsync());
    }

    [Theory]
    [InlineData("short")]                        // < 16
    [InlineData("has spaces in it 0123456789")]  // illegal characters
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")] // > 64
    public async Task A_malformed_key_is_rejected(string key)
    {
        var (orderId, _) = await CreditOrderAsync();

        var resp = await SendPaymentAsync(Admin, new { orderId, amount = 100m, method = "Cash" }, key);
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("IDEMPOTENCY_KEY_INVALID", (await resp.ReadJsonAsync()).Str("code"));
        Assert.Equal(0, await PaymentCountAsync());
    }

    [Fact]
    public async Task The_roles_that_cannot_take_payments_still_cannot_even_with_a_key()
    {
        var (orderId, _) = await CreditOrderAsync();
        await (await PayAsync(orderId, 50m, Warehouse)).ShouldBeAsync(HttpStatusCode.Forbidden);
        Assert.Equal(0, await RecordCountAsync());
    }

    // ---------------- expiry ----------------

    [Fact]
    public async Task An_expired_key_is_forgotten_and_can_be_used_again()
    {
        var (orderId, _) = await CreditOrderAsync();
        var key = NewKey();

        var first = await PayAsync(orderId, 100m, key: key);
        await first.ShouldBeAsync(HttpStatusCode.Created);
        var firstId = (await first.ReadJsonAsync()).Int("id");

        // 31 days later the row is past its retention.
        await Factory.WithDbAsync(db => db.IdempotencyRecords.ExecuteUpdateAsync(s =>
            s.SetProperty(r => r.ExpiresAt, DateTime.UtcNow.AddDays(-1))));

        var again = await PayAsync(orderId, 100m, key: key);
        await again.ShouldBeAsync(HttpStatusCode.Created);
        Assert.False(IsReplay(again));
        Assert.NotEqual(firstId, (await again.ReadJsonAsync()).Int("id"));

        Assert.Equal(2, await PaymentCountAsync());
        Assert.Equal(1, await RecordCountAsync()); // the expired row was replaced, not duplicated
    }

    // ---------------- consistency: a replay changes nothing anywhere ----------------

    [Fact]
    public async Task Replays_never_change_order_debt_or_finance()
    {
        var (orderId, customer) = await CreditOrderAsync();
        var key = NewKey();
        await (await PayAsync(orderId, 300m, key: key)).ShouldBeAsync(HttpStatusCode.Created);

        async Task<(decimal Outstanding, decimal Received, int Audits)> SnapshotAsync()
        {
            var outstanding = await Admin.GetAsync($"/api/customers/{customer}/outstanding-orders");
            await outstanding.ShouldBeAsync(HttpStatusCode.OK);
            var rows = (await outstanding.ReadJsonAsync()).EnumerateArray().ToList();

            var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd");
            var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
            var summary = await Admin.GetAsync($"/api/finance/summary?from={from}&to={to}");
            await summary.ShouldBeAsync(HttpStatusCode.OK);

            var audits = await Factory.WithDbAsync(db => db.AuditLogs.CountAsync(a => a.Action == "Payment.Create"));
            return (rows.Sum(r => r.Dec("outstanding")), (await summary.ReadJsonAsync()).Dec("paymentsReceived"), audits);
        }

        var before = await SnapshotAsync();
        Assert.Equal(500m, before.Outstanding);   // 800 - 300
        Assert.Equal(300m, before.Received);
        Assert.Equal(1, before.Audits);

        for (var i = 0; i < 5; i++)
        {
            await (await PayAsync(orderId, 300m, key: key)).ShouldBeAsync(HttpStatusCode.Created);
        }

        Assert.Equal(before, await SnapshotAsync());   // debt, payments received and the audit trail are all unchanged
        Assert.Equal(1, await PaymentCountAsync());
    }

    [Fact]
    public async Task A_payment_on_a_fully_paid_walk_in_sale_is_refused_and_does_not_use_up_its_key()
    {
        // The business rules still decide first-time requests: nothing is outstanding on a paid walk-in sale.
        var product = await SeedProductAsync(salePrice: 100m, stock: 5);
        var (orderId, total) = await CreateOrderAsync(null, 0m, Item(product, 2));
        await (await CompleteAsync(orderId, total)).ShouldBeAsync(HttpStatusCode.OK);

        await (await PayAsync(orderId, 100m)).ShouldBeAsync(HttpStatusCode.Conflict); // overpayment
        Assert.Equal(1, await PaymentCountAsync());    // only the sale's own payment
        Assert.Equal(0, await RecordCountAsync());     // the rejected request left no key behind
    }

    // ---------------- stored row ----------------

    [Fact]
    public async Task The_stored_record_points_at_the_payment_and_carries_the_request_fingerprint()
    {
        var (orderId, _) = await CreditOrderAsync();
        var key = NewKey();
        var resp = await PayAsync(orderId, 125.5m, key: key, method: "Card");
        await resp.ShouldBeAsync(HttpStatusCode.Created);
        var paymentId = (await resp.ReadJsonAsync()).Int("id");

        var row = await Factory.WithDbAsync(db => db.IdempotencyRecords.AsNoTracking().SingleAsync(r => r.Scope == "payment.create"));
        Assert.Equal("payment.create", row.Scope);
        Assert.Equal(key, row.Key);
        Assert.Equal(nameof(Payment), row.ResultEntityType);
        Assert.Equal(paymentId, row.ResultEntityId);
        Assert.Equal(PaymentRequestHash.Compute(orderId, 125.5m, PaymentMethod.Card, null, null), row.RequestHash);
        Assert.InRange((row.ExpiresAt - row.CreatedAt).TotalDays, 29.9, 30.1);
    }

    // ---------------- the fingerprint itself ----------------

    [Fact]
    public void The_fingerprint_ignores_null_vs_empty_spaces_and_trailing_zeros_but_not_real_differences()
    {
        var baseline = PaymentRequestHash.Compute(7, 300m, PaymentMethod.Cash, null, null);

        Assert.Equal(baseline, PaymentRequestHash.Compute(7, 300.00m, PaymentMethod.Cash, "", ""));
        Assert.Equal(baseline, PaymentRequestHash.Compute(7, 300m, PaymentMethod.Cash, "  ", "  "));
        Assert.Equal(64, baseline.Length);

        Assert.NotEqual(baseline, PaymentRequestHash.Compute(8, 300m, PaymentMethod.Cash, null, null));
        Assert.NotEqual(baseline, PaymentRequestHash.Compute(7, 300.01m, PaymentMethod.Cash, null, null));
        Assert.NotEqual(baseline, PaymentRequestHash.Compute(7, 300m, PaymentMethod.Card, null, null));
        Assert.NotEqual(baseline, PaymentRequestHash.Compute(7, 300m, PaymentMethod.Cash, "ref-1", null));
        Assert.NotEqual(baseline, PaymentRequestHash.Compute(7, 300m, PaymentMethod.Cash, null, "note"));
    }
}
