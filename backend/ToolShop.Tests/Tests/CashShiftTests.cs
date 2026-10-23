using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ToolShop.Domain.Entities.Cash;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Infrastructure.Cash;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// Cash shifts (Phase B): opening / closing, expected cash vs counted cash, cash in / out, cash vs card vs debt, one open shift
/// per drawer (also under concurrency), reconciliation against an independent calculation, permissions and audit.
/// </summary>
[Collection("db")]
public class CashShiftTests(ApiFactory factory) : TestBase(factory)
{
    private CashShiftOptions Options => Factory.Services.GetRequiredService<CashShiftOptions>();

    // ---------------- helpers ----------------

    private Task<HttpResponseMessage> OpenShift(HttpClient c, decimal opening) =>
        c.PostAsJsonAsync("/api/cash-shifts/open", new { openingCash = opening });

    private async Task<int> OpenShiftOk(HttpClient c, decimal opening)
    {
        var r = await OpenShift(c, opening);
        await r.ShouldBeAsync(HttpStatusCode.Created);
        return (await r.ReadJsonAsync()).Int("id");
    }

    private Task<HttpResponseMessage> CloseShift(HttpClient c, int id, decimal actual, string? note = null) =>
        c.PostAsJsonAsync($"/api/cash-shifts/{id}/close", new { actualCash = actual, note });

    private Task<HttpResponseMessage> Move(HttpClient c, int shiftId, string type, decimal amount, string reason = "test",
        string? key = null, int? expenseId = null) =>
        PostWithKeyAsync(c, $"/api/cash-shifts/{shiftId}/movements", new { type, amount, reason, expenseId }, key ?? NewKey());

    private async Task<JsonElement> GetShift(int id, HttpClient? c = null)
    {
        var r = await (c ?? Admin).GetAsync($"/api/cash-shifts/{id}");
        await r.ShouldBeAsync(HttpStatusCode.OK);
        return (await r.ReadJsonAsync()).GetProperty("shift");
    }

    private async Task<decimal> Expected(int id) => (await GetShift(id)).Dec("expectedCash");

    /// <summary>A customer order of <paramref name="quantity"/> x 100, completed on credit (nothing paid yet).</summary>
    private async Task<int> CreditOrder(int quantity = 100)
    {
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: quantity + 10);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateCompletedCreditOrderAsync(customer, product, quantity);
        return orderId;
    }

    private async Task<int> PayOk(int orderId, decimal amount, string method = "Cash", HttpClient? c = null)
    {
        var r = await PayAsync(orderId, amount, client: c, method: method);
        await r.ShouldBeAsync(HttpStatusCode.Created);
        return (await r.ReadJsonAsync()).Int("id");
    }

    // ---------------- opening ----------------

    [Fact]
    public async Task Opening_needs_an_opening_cash_value_an_empty_body_opens_nothing()
    {
        var r = await Cashier.PostAsJsonAsync("/api/cash-shifts/open", new { });
        await r.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal(0, await Factory.WithDbAsync(db => db.CashShifts.CountAsync()));
    }

    [Fact]
    public async Task A_cashier_opens_a_shift_and_a_second_open_shift_is_refused()
    {
        var id = await OpenShiftOk(Cashier, 150m);
        var shift = await GetShift(id);
        Assert.Equal("Open", shift.Str("status"));
        Assert.StartsWith("SHF-", shift.Str("shiftNumber"));
        Assert.Equal(150m, shift.Dec("openingCash"));
        Assert.Equal(JsonValueKind.Null, shift.GetProperty("closingCash").ValueKind);     // N/A, not 0
        Assert.Equal(JsonValueKind.Null, shift.GetProperty("cashDifference").ValueKind);  // N/A, not 0

        var second = await OpenShift(Manager, 10m);
        await second.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("CASH_SHIFT_ALREADY_OPEN", (await second.ReadJsonAsync()).Str("code"));
    }

    [Fact]
    public async Task Ten_parallel_openings_create_exactly_one_open_shift()
    {
        var clients = new[] { Admin, Manager, Cashier };
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => OpenShift(clients[i % 3], 100m)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await Factory.WithDbAsync(db => db.CashShifts.CountAsync(s => s.Status == CashShiftStatus.Open)));
    }

    [Fact]
    public async Task The_database_itself_refuses_a_second_open_shift_on_one_drawer()
    {
        await OpenShiftOk(Admin, 0m);

        await Assert.ThrowsAsync<DbUpdateException>(() => Factory.WithDbAsync(async db =>
        {
            var registerId = await db.CashRegisters.Select(r => r.Id).FirstAsync();
            db.CashShifts.Add(new CashShift
            {
                ShiftNumber = "SHF-RAW-1",
                CashRegisterId = registerId,
                CashierUserId = Factory.AdminUserId,
                OpenedByUserId = Factory.AdminUserId,
                OpenedAt = DateTime.UtcNow,
                OpeningCash = 0m,
                Status = CashShiftStatus.Open
            });
            await db.SaveChangesAsync();
        }));
    }

    // ---------------- reconciliation: cash vs card vs debt ----------------

    [Fact]
    public async Task Expected_cash_counts_cash_in_cash_out_and_cash_payments_but_not_card_or_debt()
    {
        var id = await OpenShiftOk(Cashier, 100m);
        var order = await CreditOrder();                       // a 10000 credit sale: a debt, not cash
        Assert.Equal(100m, await Expected(id));                // the debt changed nothing

        await PayOk(order, 300m, "Cash");
        await PayOk(order, 200m, "Card");
        (await Move(Admin, id, "CashIn", 50m, "float top-up")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Move(Admin, id, "CashOut", 30m, "bank drop")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var shift = await GetShift(id, Manager);
        Assert.Equal(420m, shift.Dec("expectedCash"));         // 100 + 300 + 50 - 30; the 200 by card is NOT cash
        var b = shift.GetProperty("breakdown");
        Assert.Equal(300m, b.Dec("cashSales"));
        Assert.Equal(200m, b.Dec("cardSales"));
        Assert.Equal(50m, b.Dec("cashIn"));
        Assert.Equal(30m, b.Dec("cashOut"));

        var close = await CloseShift(Cashier, id, 420m);
        await close.ShouldBeAsync(HttpStatusCode.OK);
        var closed = await close.ReadJsonAsync();
        Assert.Equal("Closed", closed.Str("status"));
        Assert.Equal(0m, closed.Dec("cashDifference"));
        Assert.False(closed.GetProperty("requiresReview").GetBoolean());
        Assert.Equal(420m, closed.Dec("expectedCash"));
    }

    [Fact]
    public async Task A_voided_cash_payment_drops_out_of_the_expected_cash()
    {
        var id = await OpenShiftOk(Admin, 0m);
        var order = await CreditOrder();
        var paymentId = await PayOk(order, 300m);
        Assert.Equal(300m, await Expected(id));

        var voided = await Manager.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "wrong amount" });
        await voided.ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(0m, await Expected(id));
    }

    [Fact]
    public async Task A_payment_of_an_earlier_closed_shift_voided_in_a_later_shift_is_taken_out_of_the_later_drawer()
    {
        var a = await OpenShiftOk(Admin, 0m);
        var order = await CreditOrder();
        var paymentId = await PayOk(order, 200m);
        await (await CloseShift(Admin, a, 200m)).ShouldBeAsync(HttpStatusCode.OK);

        var b = await OpenShiftOk(Admin, 300m);
        await (await Manager.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "customer cancelled" }))
            .ShouldBeAsync(HttpStatusCode.OK);

        Assert.Equal(100m, await Expected(b));                 // 300 - the 200 handed back
        var closedA = await GetShift(a);
        Assert.Equal(200m, closedA.Dec("expectedCash"));       // the closed shift never changes
    }

    [Fact]
    public async Task A_cash_refund_of_a_return_comes_out_of_the_drawer()
    {
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 20);
        var customer = await SeedCustomerAsync();
        var id = await OpenShiftOk(Admin, 0m);

        var (orderId, total) = await CreateOrderAsync(customer, 0m, Item(product, 5));
        await (await CompleteAsync(orderId, total)).ShouldBeAsync(HttpStatusCode.OK);    // 500 in cash
        Assert.Equal(500m, await Expected(id));

        var itemId = await Factory.WithDbAsync(db =>
            db.OrderItems.Where(i => i.OrderId == orderId).Select(i => i.Id).SingleAsync());
        var ret = await PostWithKeyAsync(Admin, $"/api/orders/{orderId}/return",
            new { reason = "returned", items = new[] { new { orderItemId = itemId, quantity = 2, damagedQuantity = 0 } } }, NewKey());
        await ret.ShouldBeAsync(HttpStatusCode.OK);                                      // refund 200 in cash

        Assert.Equal(300m, await Expected(id));
    }

    // ---------------- closing ----------------

    [Fact]
    public async Task A_difference_needs_a_note_and_then_waits_for_a_manager_review()
    {
        var id = await OpenShiftOk(Cashier, 100m);

        var noNote = await CloseShift(Cashier, id, 80m);
        await noNote.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("CASH_DIFFERENCE_NOTE_REQUIRED", (await noNote.ReadJsonAsync()).Str("code"));
        Assert.Equal("Open", (await GetShift(id)).Str("status"));                       // nothing happened

        var closed = await CloseShift(Cashier, id, 80m, "counted twice");
        await closed.ShouldBeAsync(HttpStatusCode.OK);
        var json = await closed.ReadJsonAsync();
        Assert.Equal(-20m, json.Dec("cashDifference"));
        Assert.Equal(100m, json.Dec("expectedCash"));
        Assert.True(json.GetProperty("requiresReview").GetBoolean());
        Assert.False(json.GetProperty("reviewed").GetBoolean());

        (await Cashier.PostAsJsonAsync($"/api/cash-shifts/{id}/review", new { note = "ok" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var review = await Manager.PostAsJsonAsync($"/api/cash-shifts/{id}/review", new { note = "accepted, short 20" });
        await review.ShouldBeAsync(HttpStatusCode.OK);
        Assert.True((await review.ReadJsonAsync()).GetProperty("reviewed").GetBoolean());

        (await Manager.PostAsJsonAsync($"/api/cash-shifts/{id}/review", new { })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_closed_shift_accepts_nothing_more()
    {
        var id = await OpenShiftOk(Admin, 100m);
        await (await CloseShift(Admin, id, 100m)).ShouldBeAsync(HttpStatusCode.OK);

        (await CloseShift(Admin, id, 100m)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var move = await Move(Admin, id, "CashIn", 10m);
        await move.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("CASH_SHIFT_NOT_OPEN", (await move.ReadJsonAsync()).Str("code"));
    }

    [Fact]
    public async Task A_cashier_cannot_close_another_cashiers_shift_but_a_manager_can()
    {
        var id = await OpenShiftOk(Admin, 100m);               // the shift belongs to the admin

        (await CloseShift(Cashier, id, 100m)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var byManager = await CloseShift(Manager, id, 100m);
        await byManager.ShouldBeAsync(HttpStatusCode.OK);
        Assert.True((await byManager.ReadJsonAsync()).GetProperty("closedByOther").GetBoolean());
    }

    [Fact]
    public async Task While_the_shift_is_open_the_cashier_does_not_see_the_expected_cash_but_a_manager_does()
    {
        var id = await OpenShiftOk(Cashier, 100m);

        var asCashier = await GetShift(id, Cashier);
        Assert.Equal(JsonValueKind.Null, asCashier.GetProperty("expectedCash").ValueKind);
        Assert.Equal(JsonValueKind.Null, asCashier.GetProperty("breakdown").GetProperty("cashSales").ValueKind);

        Assert.Equal(100m, (await GetShift(id, Manager)).Dec("expectedCash"));
    }

    // ---------------- cash in / out ----------------

    [Fact]
    public async Task A_movement_needs_a_reason_and_a_positive_amount()
    {
        var id = await OpenShiftOk(Admin, 100m);
        (await Move(Admin, id, "CashIn", 10m, reason: " ")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Move(Admin, id, "CashIn", 0m)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Move(Admin, id, "CashIn", -5m)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal(0, await Factory.WithDbAsync(db => db.CashMovements.CountAsync()));
    }

    [Fact]
    public async Task The_same_idempotency_key_records_one_movement_and_a_different_request_under_it_is_a_conflict()
    {
        var id = await OpenShiftOk(Admin, 100m);
        var key = NewKey();

        var first = await Move(Admin, id, "CashIn", 40m, "top-up", key);
        await first.ShouldBeAsync(HttpStatusCode.Created);
        var second = await Move(Admin, id, "CashIn", 40m, "top-up", key);
        await second.ShouldBeAsync(HttpStatusCode.Created);
        Assert.True(second.Headers.TryGetValues("Idempotent-Replay", out var v) && v.Contains("true"));
        Assert.Equal((await first.ReadJsonAsync()).Int("id"), (await second.ReadJsonAsync()).Int("id"));
        Assert.Equal(1, await Factory.WithDbAsync(db => db.CashMovements.CountAsync()));
        Assert.Equal(140m, await Expected(id));

        (await Move(Admin, id, "CashIn", 41m, "top-up", key)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Move(Admin, id, "CashIn", 5m, "x", key: null)).StatusCode.ShouldBe(HttpStatusCode.Created);   // sanity: new key works
    }

    [Fact]
    public async Task A_movement_without_an_idempotency_key_is_refused()
    {
        var id = await OpenShiftOk(Admin, 100m);
        var r = await PostWithKeyAsync(Admin, $"/api/cash-shifts/{id}/movements",
            new { type = "CashIn", amount = 10m, reason = "x" }, key: null);
        await r.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", (await r.ReadJsonAsync()).Str("code"));
    }

    [Fact]
    public async Task A_cashier_has_a_limit_per_movement_a_manager_does_not()
    {
        var id = await OpenShiftOk(Cashier, 1000m);

        var over = await Move(Cashier, id, "CashIn", Options.CashierMovementLimit + 1m);
        await over.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("CASH_MOVEMENT_LIMIT", (await over.ReadJsonAsync()).Str("code"));

        (await Move(Cashier, id, "CashIn", Options.CashierMovementLimit)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Move(Manager, id, "CashIn", Options.CashierMovementLimit + 1m)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Cash_cannot_be_taken_out_beyond_what_the_drawer_should_hold()
    {
        var id = await OpenShiftOk(Admin, 50m);
        var r = await Move(Admin, id, "CashOut", 80m, "too much");
        await r.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("CASH_OUT_EXCEEDS_DRAWER", (await r.ReadJsonAsync()).Str("code"));
        Assert.Equal(50m, await Expected(id));
    }

    [Fact]
    public async Task Two_parallel_cash_outs_that_together_exceed_the_drawer_let_only_one_through()
    {
        var id = await OpenShiftOk(Admin, 100m);

        var responses = await Task.WhenAll(
            Move(Admin, id, "CashOut", 80m, "a"),
            Move(Manager, id, "CashOut", 80m, "b"));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(20m, await Expected(id));
    }

    [Fact]
    public async Task Voiding_a_movement_restores_the_expected_cash_only_for_a_manager_and_only_while_open()
    {
        var id = await OpenShiftOk(Admin, 100m);
        var move = await Move(Admin, id, "CashOut", 30m, "supplier");
        await move.ShouldBeAsync(HttpStatusCode.Created);
        var moveId = (await move.ReadJsonAsync()).Int("id");
        Assert.Equal(70m, await Expected(id));

        (await Cashier.PostAsJsonAsync($"/api/cash-shifts/movements/{moveId}/void", new { reason = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await (await Manager.PostAsJsonAsync($"/api/cash-shifts/movements/{moveId}/void", new { reason = "entered by mistake" }))
            .ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(100m, await Expected(id));

        (await Manager.PostAsJsonAsync($"/api/cash-shifts/movements/{moveId}/void", new { reason = "again" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var other = await Move(Admin, id, "CashIn", 10m);
        var otherId = (await other.ReadJsonAsync()).Int("id");
        await (await CloseShift(Admin, id, 110m)).ShouldBeAsync(HttpStatusCode.OK);
        (await Manager.PostAsJsonAsync($"/api/cash-shifts/movements/{otherId}/void", new { reason = "late" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ---------------- requiring an open shift ----------------

    [Fact]
    public async Task With_the_requirement_on_a_payment_without_an_open_shift_is_refused_and_its_key_is_not_burned()
    {
        var order = await CreditOrder();
        Options.RequireOpenShift = true;
        try
        {
            var key = NewKey();
            var refused = await PayAsync(order, 100m, key: key);
            await refused.ShouldBeAsync(HttpStatusCode.Conflict);
            Assert.Equal("NO_OPEN_CASH_SHIFT", (await refused.ReadJsonAsync()).Str("code"));
            Assert.Equal(0, await PaymentCountAsync());

            await OpenShiftOk(Admin, 0m);
            await (await PayAsync(order, 100m, key: key)).ShouldBeAsync(HttpStatusCode.Created);   // same key works now
            Assert.Equal(1, await PaymentCountAsync());
        }
        finally
        {
            Options.RequireOpenShift = false;
        }
    }

    [Fact]
    public async Task With_the_requirement_on_a_sale_paid_at_once_needs_an_open_shift_and_the_order_stays_a_draft()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var (orderId, total) = await CreateOrderAsync(null, 0m, Item(product, 1));
        Options.RequireOpenShift = true;
        try
        {
            var refused = await CompleteAsync(orderId, total);
            await refused.ShouldBeAsync(HttpStatusCode.Conflict);
            Assert.Equal("NO_OPEN_CASH_SHIFT", (await refused.ReadJsonAsync()).Str("code"));
            Assert.Equal(Domain_Draft, await OrderStatusAsync(orderId));
            Assert.Equal(10, await StockAsync(product));          // nothing was sold

            await OpenShiftOk(Admin, 0m);
            await (await CompleteAsync(orderId, total)).ShouldBeAsync(HttpStatusCode.OK);
            Assert.Equal(9, await StockAsync(product));
        }
        finally
        {
            Options.RequireOpenShift = false;
        }
    }

    private static readonly ToolShop.Domain.Entities.Sales.OrderStatus Domain_Draft = ToolShop.Domain.Entities.Sales.OrderStatus.Draft;

    [Fact]
    public async Task Without_the_requirement_a_payment_with_no_open_shift_still_works_and_belongs_to_no_shift()
    {
        var order = await CreditOrder();
        var paymentId = await PayOk(order, 100m);
        Assert.Equal(0, await Factory.WithDbAsync(db => db.CashShiftPayments.CountAsync(l => l.PaymentId == paymentId)));
    }

    [Fact]
    public async Task When_the_feature_is_switched_off_nothing_is_linked_and_the_endpoints_say_so()
    {
        var order = await CreditOrder();
        await OpenShiftOk(Admin, 0m);
        Options.Enabled = false;
        try
        {
            var paymentId = await PayOk(order, 100m);
            Assert.Equal(0, await Factory.WithDbAsync(db => db.CashShiftPayments.CountAsync(l => l.PaymentId == paymentId)));

            var current = await Admin.GetAsync("/api/cash-shifts/current");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, current.StatusCode);
            Assert.Equal("CASH_SHIFTS_DISABLED", (await current.ReadJsonAsync()).Str("code"));
        }
        finally
        {
            Options.Enabled = true;
        }
    }

    // ---------------- concurrency ----------------

    [Fact]
    public async Task Payments_racing_with_the_closing_are_never_counted_in_a_shift_that_did_not_include_them()
    {
        var id = await OpenShiftOk(Admin, 10m);
        var orders = new List<int>();
        for (var i = 0; i < 6; i++) orders.Add(await CreditOrder(quantity: 5));

        var payments = orders.Select(o => PayAsync(o, 100m)).ToList();
        var close = CloseShift(Admin, id, 0m, "race test");
        await Task.WhenAll(payments.Append(close));

        await (await close).ShouldBeAsync(HttpStatusCode.OK);

        // The snapshot taken at closing equals the opening plus exactly the cash payments linked to the shift.
        var (snapshot, linked) = await Factory.WithDbAsync(async db =>
        {
            var shift = await db.CashShifts.AsNoTracking().FirstAsync(s => s.Id == id);
            var sum = await db.Payments.AsNoTracking()
                .Where(p => p.Status == PaymentStatus.Completed && p.Method == PaymentMethod.Cash
                            && db.CashShiftPayments.Any(l => l.PaymentId == p.Id && l.CashShiftId == id))
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            return (shift.ExpectedCashAtClose!.Value, sum);
        });
        Assert.Equal(10m + linked, snapshot);

        // Whatever was paid after the closing belongs to no shift.
        var strays = await Factory.WithDbAsync(db => db.Payments.CountAsync(p =>
            !db.CashShiftPayments.Any(l => l.PaymentId == p.Id)));
        var created = await PaymentCountAsync();
        var linkedCount = await Factory.WithDbAsync(db => db.CashShiftPayments.CountAsync(l => l.CashShiftId == id));
        Assert.Equal(created, linkedCount + strays);
    }

    // ---------------- reconciliation against an independent calculation ----------------

    [Fact]
    public async Task A_random_day_reconciles_with_an_independent_calculation()
    {
        var rng = new Random(20261023);
        var opening = 250m;
        var id = await OpenShiftOk(Admin, opening);
        var order = await CreditOrder(quantity: 100);          // 10000 outstanding: room for every payment below

        var expected = opening;
        var livePayments = new List<(int Id, decimal Amount)>();

        for (var step = 0; step < 25; step++)
        {
            switch (rng.Next(5))
            {
                case 0:
                case 1:
                {
                    var amount = rng.Next(1, 40) * 5m;
                    var pid = await PayOk(order, amount, "Cash");
                    livePayments.Add((pid, amount));
                    expected += amount;
                    break;
                }
                case 2:
                {
                    await PayOk(order, rng.Next(1, 40) * 5m, "Card");   // not cash
                    break;
                }
                case 3:
                {
                    var amount = rng.Next(1, 30) * 5m;
                    if (rng.Next(2) == 0)
                    {
                        await (await Move(Admin, id, "CashIn", amount, "in")).ShouldBeAsync(HttpStatusCode.Created);
                        expected += amount;
                    }
                    else if (amount <= expected)
                    {
                        await (await Move(Admin, id, "CashOut", amount, "out")).ShouldBeAsync(HttpStatusCode.Created);
                        expected -= amount;
                    }
                    break;
                }
                default:
                {
                    if (livePayments.Count == 0) break;
                    var pick = livePayments[rng.Next(livePayments.Count)];
                    await (await Manager.PostAsJsonAsync($"/api/payments/{pick.Id}/void", new { reason = "random void" }))
                        .ShouldBeAsync(HttpStatusCode.OK);
                    livePayments.Remove(pick);
                    expected -= pick.Amount;
                    break;
                }
            }
        }

        Assert.Equal(expected, await Expected(id));

        var close = await CloseShift(Admin, id, expected);
        await close.ShouldBeAsync(HttpStatusCode.OK);
        var json = await close.ReadJsonAsync();
        Assert.Equal(expected, json.Dec("expectedCash"));
        Assert.Equal(0m, json.Dec("cashDifference"));
    }

    // ---------------- audit and permissions ----------------

    [Fact]
    public async Task Opening_closing_and_cash_movements_are_written_to_the_audit_log()
    {
        var id = await OpenShiftOk(Cashier, 100m);
        await (await Move(Admin, id, "CashIn", 20m, "top-up")).ShouldBeAsync(HttpStatusCode.Created);
        await (await CloseShift(Cashier, id, 120m)).ShouldBeAsync(HttpStatusCode.OK);

        var actions = await Factory.WithDbAsync(db =>
            db.AuditLogs.Where(a => a.EntityName == "CashShift").Select(a => a.Action).ToListAsync());
        Assert.Contains("CashShift.Open", actions);
        Assert.Contains("CashShift.CashIn", actions);
        Assert.Contains("CashShift.Close", actions);
    }

    [Fact]
    public async Task The_warehouse_role_has_nothing_to_do_with_cash_shifts()
    {
        (await Warehouse.GetAsync("/api/cash-shifts/current")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Warehouse.PostAsJsonAsync("/api/cash-shifts/open", new { openingCash = 0m })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_cashier_lists_only_their_own_shifts_a_manager_lists_all()
    {
        var mine = await OpenShiftOk(Cashier, 0m);
        await (await CloseShift(Cashier, mine, 0m)).ShouldBeAsync(HttpStatusCode.OK);
        var others = await OpenShiftOk(Admin, 0m);
        await (await CloseShift(Admin, others, 0m)).ShouldBeAsync(HttpStatusCode.OK);

        var asCashier = await (await Cashier.GetAsync("/api/cash-shifts")).ReadJsonAsync();
        Assert.Equal(1, asCashier.GetProperty("totalItems").GetInt32());
        var asManager = await (await Manager.GetAsync("/api/cash-shifts")).ReadJsonAsync();
        Assert.Equal(2, asManager.GetProperty("totalItems").GetInt32());

        (await Cashier.GetAsync($"/api/cash-shifts/{others}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

internal static class StatusAssertions
{
    /// <summary>Readable one-line assertion on a status code.</summary>
    public static void ShouldBe(this HttpStatusCode actual, HttpStatusCode expected) => Assert.Equal(expected, actual);
}
