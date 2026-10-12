using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// Debt is never stored: debt = Order.TotalAmount - valid (Completed) payments, for COMPLETED orders only.
/// The Debts page, Finance and Dashboard must all agree.
/// </summary>
[Collection("db")]
public class DebtTests(ApiFactory factory) : TestBase(factory)
{
    private async Task<JsonElement> GetDebtsAsync()
    {
        var resp = await Admin.GetAsync("/api/debts?pageSize=100");
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        return await resp.ReadJsonAsync();
    }

    private static decimal? DebtOf(JsonElement debts, int customerId)
    {
        foreach (var item in debts.GetProperty("items").EnumerateArray())
            if (item.Int("customerId") == customerId) return item.Dec("totalDebt");
        return null;
    }

    private static decimal DebtLinesTotal(JsonElement debts) =>
        debts.GetProperty("items").EnumerateArray().Sum(i => i.Dec("totalDebt"));

    private async Task<decimal> FinanceReceivablesAsync()
    {
        var resp = await Admin.GetAsync("/api/finance/summary");
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        return (await resp.ReadJsonAsync()).Dec("accountsReceivable");
    }

    private async Task<decimal> DashboardReceivablesAsync()
    {
        var resp = await Admin.GetAsync("/api/dashboard/summary");
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        return (await resp.ReadJsonAsync()).Dec("accountsReceivable");
    }

    [Fact]
    public async Task Debt_is_total_minus_valid_payments_and_voiding_restores_it()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 20);
        var customer = await SeedCustomerAsync();
        var (orderId, total) = await CreateCompletedCreditOrderAsync(customer, product, 5);
        Assert.Equal(500m, total);

        Assert.Equal(500m, DebtOf(await GetDebtsAsync(), customer));

        var paid = await PayAsync(orderId, 200m);
        await paid.ShouldBeAsync(HttpStatusCode.Created);
        var paymentId = (await paid.ReadJsonAsync()).Int("id");
        Assert.Equal(300m, DebtOf(await GetDebtsAsync(), customer));

        await (await Admin.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "debt test" }))
            .ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(500m, DebtOf(await GetDebtsAsync(), customer)); // voided payment no longer counts
    }

    [Fact]
    public async Task Fully_paid_draft_and_cancelled_orders_create_no_debt()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 20);

        var paidCustomer = await SeedCustomerAsync();
        var (paidOrder, _) = await CreateCompletedCreditOrderAsync(paidCustomer, product, 2);
        await (await PayAsync(paidOrder, 200m)).ShouldBeAsync(HttpStatusCode.Created);

        var draftCustomer = await SeedCustomerAsync();
        await CreateOrderAsync(draftCustomer, 0m, Item(product, 1)); // stays Draft

        var cancelledCustomer = await SeedCustomerAsync();
        var (cancelledOrder, _) = await CreateOrderAsync(cancelledCustomer, 0m, Item(product, 1));
        await (await Admin.PostAsync($"/api/orders/{cancelledOrder}/cancel", null))
            .ShouldBeAsync(HttpStatusCode.NoContent);

        var debts = await GetDebtsAsync();
        Assert.Null(DebtOf(debts, paidCustomer));
        Assert.Null(DebtOf(debts, draftCustomer));
        Assert.Null(DebtOf(debts, cancelledCustomer));
        Assert.Equal(0m, DebtLinesTotal(debts));
        Assert.Equal(0m, await FinanceReceivablesAsync());
        Assert.Equal(0m, await DashboardReceivablesAsync());
    }

    [Fact]
    public async Task Inactive_customer_still_owes_their_debt()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();
        await CreateCompletedCreditOrderAsync(customer, product, 2);

        await Factory.WithDbAsync(async db =>
        {
            var c = await db.Customers.FirstAsync(x => x.Id == customer);
            c.IsActive = false; // deactivate, never delete
            await db.SaveChangesAsync();
        });

        Assert.Equal(200m, DebtOf(await GetDebtsAsync(), customer));
        Assert.Equal(200m, await FinanceReceivablesAsync());
    }

    [Fact]
    public async Task Debts_Finance_and_Dashboard_agree_including_historical_walk_in_debt()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 50);

        var customerA = await SeedCustomerAsync();
        await CreateCompletedCreditOrderAsync(customerA, product, 3);              // owes 300

        var customerB = await SeedCustomerAsync();
        var (orderB, _) = await CreateCompletedCreditOrderAsync(customerB, product, 2); // total 200
        await (await PayAsync(orderB, 50m)).ShouldBeAsync(HttpStatusCode.Created);       // owes 150

        await SeedHistoricalWalkInOrderAsync(total: 400m, paid: 250m);                   // walk-in owes 150

        var debts = await GetDebtsAsync();
        Assert.Equal(300m, DebtOf(debts, customerA));
        Assert.Equal(150m, DebtOf(debts, customerB));
        Assert.Equal(450m, DebtLinesTotal(debts));
        Assert.Equal(150m, debts.Dec("walkInUnresolvedDebt"));
        Assert.Equal(1, debts.Int("walkInUnresolvedOrderCount"));

        // Walk-in exceptions list (read-only) matches the reconciliation figure.
        var ex = await Admin.GetAsync("/api/debts/walk-in-exceptions");
        await ex.ShouldBeAsync(HttpStatusCode.OK);
        var exj = await ex.ReadJsonAsync();
        Assert.Equal(150m, exj.Dec("totalOutstanding"));
        Assert.Equal(1, exj.Int("count"));

        // Finance and Dashboard receivables = customer debt + walk-in debt, identical on both screens.
        var expected = 450m + 150m;
        Assert.Equal(expected, await FinanceReceivablesAsync());
        Assert.Equal(expected, await DashboardReceivablesAsync());
    }

    [Fact]
    public async Task Voiding_a_payment_moves_Debts_Finance_and_Dashboard_together()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateCompletedCreditOrderAsync(customer, product, 3); // 300

        var paid = await PayAsync(orderId, 300m);
        await paid.ShouldBeAsync(HttpStatusCode.Created);
        var paymentId = (await paid.ReadJsonAsync()).Int("id");

        Assert.Equal(0m, DebtLinesTotal(await GetDebtsAsync()));
        Assert.Equal(0m, await FinanceReceivablesAsync());
        Assert.Equal(0m, await DashboardReceivablesAsync());

        await (await Admin.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "consistency" }))
            .ShouldBeAsync(HttpStatusCode.OK);

        Assert.Equal(300m, DebtLinesTotal(await GetDebtsAsync()));
        Assert.Equal(300m, await FinanceReceivablesAsync());
        Assert.Equal(300m, await DashboardReceivablesAsync());
    }

    [Fact]
    public async Task Walk_in_exceptions_endpoint_is_read_only_and_restricted()
    {
        await SeedHistoricalWalkInOrderAsync(total: 100m, paid: 0m);
        var before = await PaymentCountAsync();

        await (await Cashier.GetAsync("/api/debts/walk-in-exceptions")).ShouldBeAsync(HttpStatusCode.Forbidden);
        await (await Manager.GetAsync("/api/debts/walk-in-exceptions")).ShouldBeAsync(HttpStatusCode.OK);
        await (await Anonymous.GetAsync("/api/debts/walk-in-exceptions")).ShouldBeAsync(HttpStatusCode.Unauthorized);

        Assert.Equal(before, await PaymentCountAsync()); // reading changes nothing
    }
}
