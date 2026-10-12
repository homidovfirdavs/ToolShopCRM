using System.Net;
using System.Net.Http.Json;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>Payments: partial/full settlement, overpayment, voiding, concurrency, role rules.</summary>
[Collection("db")]
public class PaymentTests(ApiFactory factory) : TestBase(factory)
{
    [Fact]
    public async Task Partial_then_full_payment_settles_the_order()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 20);
        var customer = await SeedCustomerAsync();
        var (orderId, total) = await CreateCompletedCreditOrderAsync(customer, product, quantity: 3);
        Assert.Equal(300m, total);

        var first = await PayAsync(orderId, 100m);
        await first.ShouldBeAsync(HttpStatusCode.Created);
        var j1 = await first.ReadJsonAsync();
        Assert.Equal(100m, j1.Dec("totalPaid"));
        Assert.Equal(200m, j1.Dec("outstandingBalance"));
        Assert.Equal("PartiallyPaid", j1.Str("paymentState"));

        var second = await PayAsync(orderId, 200m);
        await second.ShouldBeAsync(HttpStatusCode.Created);
        var j2 = await second.ReadJsonAsync();
        Assert.Equal(300m, j2.Dec("totalPaid"));
        Assert.Equal(0m, j2.Dec("outstandingBalance"));
        Assert.Equal("Paid", j2.Str("paymentState"));
    }

    [Fact]
    public async Task Overpayment_is_rejected_with_409_and_nothing_is_saved()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateCompletedCreditOrderAsync(customer, product, quantity: 3); // total 300

        await (await PayAsync(orderId, 100m)).ShouldBeAsync(HttpStatusCode.Created);
        var over = await PayAsync(orderId, 201m); // outstanding is 200
        await over.ShouldBeAsync(HttpStatusCode.Conflict);

        Assert.Equal(1, await PaymentCountAsync());
        Assert.Equal(100m, await PaidSumAsync(orderId));
    }

    [Fact]
    public async Task Payment_on_a_draft_order_is_rejected()
    {
        var product = await SeedProductAsync(stock: 10);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateOrderAsync(customer, 0m, Item(product, 1));

        await (await PayAsync(orderId, 50m)).ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal(0, await PaymentCountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Zero_or_negative_amount_is_rejected(int amount)
    {
        var product = await SeedProductAsync(stock: 10);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateCompletedCreditOrderAsync(customer, product, 1);

        await (await PayAsync(orderId, amount)).ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal(0, await PaymentCountAsync());
    }

    [Fact]
    public async Task Payment_on_unknown_order_returns_404()
    {
        await (await PayAsync(orderId: 999_999, amount: 10m)).ShouldBeAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Voiding_a_payment_restores_the_debt_and_cannot_be_repeated()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateCompletedCreditOrderAsync(customer, product, 3); // 300

        var paid = await PayAsync(orderId, 300m);
        await paid.ShouldBeAsync(HttpStatusCode.Created);
        var paymentId = (await paid.ReadJsonAsync()).Int("id");

        var voided = await Admin.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "test void" });
        await voided.ShouldBeAsync(HttpStatusCode.OK);
        var vj = await voided.ReadJsonAsync();
        Assert.Equal("Voided", vj.Str("status"));
        Assert.Equal(300m, vj.Dec("outstandingBalance"));
        Assert.Equal(0m, await PaidSumAsync(orderId)); // voided payments do not count

        var again = await Admin.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "again" });
        await again.ShouldBeAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Void_requires_a_reason()
    {
        var product = await SeedProductAsync(stock: 10);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateCompletedCreditOrderAsync(customer, product, 1);
        var paymentId = (await (await PayAsync(orderId, 10m)).ReadJsonAsync()).Int("id");

        var resp = await Admin.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "" });
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cashier_cannot_void_but_can_take_payment_and_warehouse_cannot_take_payment()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateCompletedCreditOrderAsync(customer, product, 2); // 200

        await (await PayAsync(orderId, 50m, Warehouse)).ShouldBeAsync(HttpStatusCode.Forbidden);

        var ok = await PayAsync(orderId, 50m, Cashier);
        await ok.ShouldBeAsync(HttpStatusCode.Created);
        var paymentId = (await ok.ReadJsonAsync()).Int("id");

        var denied = await Cashier.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "x" });
        await denied.ShouldBeAsync(HttpStatusCode.Forbidden);
        Assert.Equal(50m, await PaidSumAsync(orderId)); // still valid

        var allowed = await Manager.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "manager ok" });
        await allowed.ShouldBeAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Concurrent_payments_never_exceed_the_order_total()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();
        var (orderId, total) = await CreateCompletedCreditOrderAsync(customer, product, 1); // 100
        Assert.Equal(100m, total);

        // 10 parallel payments of 30: at most 3 can fit (90); the 4th would exceed the 10 left.
        var tasks = Enumerable.Range(0, 10).Select(_ => PayAsync(orderId, 30m)).ToArray();
        var responses = await Task.WhenAll(tasks);

        var statuses = responses.Select(r => r.StatusCode).ToList();
        Assert.All(statuses, s => Assert.Contains(s,
            new[] { HttpStatusCode.Created, HttpStatusCode.Conflict, HttpStatusCode.InternalServerError }));

        var created = statuses.Count(s => s == HttpStatusCode.Created);
        Assert.InRange(created, 1, 3);

        var paid = await PaidSumAsync(orderId);
        Assert.True(paid <= 100m, $"Paid {paid} exceeds the order total 100.");
        Assert.Equal(30m * created, paid); // every 201 is really stored, nothing else is
        Assert.Equal(created, await PaymentCountAsync());
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        await (await Anonymous.GetAsync("/api/payments")).ShouldBeAsync(HttpStatusCode.Unauthorized);
        var post = await Anonymous.PostAsJsonAsync("/api/payments", new { orderId = 1, amount = 1, method = "Cash" });
        await post.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }
}
