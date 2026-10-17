using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Infrastructure.Finance;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// Top-7 - Dashboard "Best-selling" (by net quantity) and "Most profitable" (by product gross profit).
/// They reuse the Product Performance numbers (net of returns, CostAtSale, shop-local days): no formula of their own.
/// A product whose profit is N/A never enters the profit ranking and is never shown as 0.
/// </summary>
[Collection("db")]
public class TopProductsTests(ApiFactory factory) : TestBase(factory)
{
    private static string Range()
    {
        var today = ReportingClock.LocalToday();
        return $"from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}";
    }

    private async Task<JsonElement> TopAsync(string extra = "")
    {
        var resp = await Admin.GetAsync($"/api/dashboard/top-products?{Range()}{extra}");
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        return await resp.ReadJsonAsync();
    }

    private async Task<(int OrderId, int ItemId)> SellAsync(int productId, int quantity)
    {
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 0m, Item(productId, quantity));
        await (await CompleteAsync(id, total)).ShouldBeAsync(HttpStatusCode.OK);
        var itemId = await Factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == id).Select(i => i.Id).SingleAsync());
        return (id, itemId);
    }

    private static List<int> Ids(JsonElement list) => list.EnumerateArray().Select(x => x.Int("productId")).ToList();

    [Fact]
    public async Task Best_selling_is_ordered_by_quantity_and_profit_by_gross_profit_and_they_can_differ()
    {
        // A: 10 pcs, profit 10 x (100-90) = 100.   B: 2 pcs, profit 2 x (100-10) = 180.
        var a = await SeedProductAsync(salePrice: 100m, purchasePrice: 90m, stock: 50);
        var b = await SeedProductAsync(salePrice: 100m, purchasePrice: 10m, stock: 50);
        await SellAsync(a, 10);
        await SellAsync(b, 2);

        var top = await TopAsync();
        Assert.Equal(new[] { a, b }, Ids(top.GetProperty("bestSelling")));
        Assert.Equal(new[] { b, a }, Ids(top.GetProperty("mostProfitable")));
        Assert.Equal(180m, top.GetProperty("mostProfitable")[0].Dec("grossProfit"));
        Assert.Equal(10, top.GetProperty("bestSelling")[0].Int("quantitySold"));
    }

    [Fact]
    public async Task Returned_units_are_taken_off_the_quantity_and_a_fully_returned_product_disappears()
    {
        var a = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 50);
        var b = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 50);
        var c = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 50);

        var (aOrder, aItem) = await SellAsync(a, 10);
        await (await PostWithKeyAsync(Admin, $"/api/orders/{aOrder}/return",
            new { reason = "Customer returned goods", items = new[] { new { orderItemId = aItem, quantity = 8, damagedQuantity = 0 } } },
            NewKey())).ShouldBeAsync(HttpStatusCode.OK);                       // net 2
        var (cOrder, cItem) = await SellAsync(c, 5);
        await (await PostWithKeyAsync(Admin, $"/api/orders/{cOrder}/return",
            new { reason = "Customer returned goods", items = new[] { new { orderItemId = cItem, quantity = 5, damagedQuantity = 0 } } },
            NewKey())).ShouldBeAsync(HttpStatusCode.OK);                       // net 0
        await SellAsync(b, 3);

        var top = await TopAsync();
        Assert.Equal(new[] { b, a }, Ids(top.GetProperty("bestSelling")));            // 3 then 2; c is gone
        Assert.Equal(2, top.GetProperty("bestSelling")[1].Int("quantitySold"));
        Assert.DoesNotContain(c, Ids(top.GetProperty("mostProfitable")));
    }

    [Fact]
    public async Task A_product_with_unknown_cost_is_never_ranked_by_profit_and_is_listed_as_N_A_not_zero()
    {
        var known = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 20);
        var unknown = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 20, uncostedStock: true);
        await SellAsync(known, 1);
        await SellAsync(unknown, 7);

        var top = await TopAsync();
        Assert.Equal(new[] { unknown, known }, Ids(top.GetProperty("bestSelling")));  // quantity does not need a cost
        Assert.Equal(new[] { known }, Ids(top.GetProperty("mostProfitable")));
        var unknownRows = top.GetProperty("unknownProfit");
        Assert.Equal(new[] { unknown }, Ids(unknownRows));
        Assert.Equal(JsonValueKind.Null, unknownRows[0].GetProperty("grossProfit").ValueKind);   // null, not 0
    }

    [Fact]
    public async Task A_loss_making_product_is_not_most_profitable()
    {
        var loss = await SeedProductAsync(salePrice: 100m, purchasePrice: 150m, stock: 20);
        await SellAsync(loss, 3);

        var top = await TopAsync();
        Assert.Empty(top.GetProperty("mostProfitable").EnumerateArray());
        Assert.Equal(new[] { loss }, Ids(top.GetProperty("bestSelling")));
    }

    [Fact]
    public async Task With_no_sales_all_three_lists_are_empty()
    {
        await SeedProductAsync(stock: 10);

        var top = await TopAsync();
        Assert.Empty(top.GetProperty("bestSelling").EnumerateArray());
        Assert.Empty(top.GetProperty("mostProfitable").EnumerateArray());
        Assert.Empty(top.GetProperty("unknownProfit").EnumerateArray());
    }

    [Fact]
    public async Task Top_limits_the_lists()
    {
        for (var i = 0; i < 4; i++)
        {
            var p = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 20);
            await SellAsync(p, i + 1);
        }

        var top = await TopAsync("&top=2");
        Assert.Equal(2, top.GetProperty("bestSelling").GetArrayLength());
        Assert.Equal(2, top.GetProperty("mostProfitable").GetArrayLength());
    }

    [Fact]
    public async Task The_date_filter_uses_the_shop_local_day()
    {
        var p = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 20);
        var (id, _) = await SellAsync(p, 1);
        // 10 March 21:00 UTC = 11 March 02:00 local.
        await Factory.WithDbAsync(db => db.Orders.Where(o => o.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDate, new DateTime(2026, 3, 10, 21, 0, 0, DateTimeKind.Utc))));

        var onTenth = await (await Admin.GetAsync("/api/dashboard/top-products?from=2026-03-10&to=2026-03-10")).ReadJsonAsync();
        var onEleventh = await (await Admin.GetAsync("/api/dashboard/top-products?from=2026-03-11&to=2026-03-11")).ReadJsonAsync();
        Assert.Empty(onTenth.GetProperty("bestSelling").EnumerateArray());
        Assert.Equal(new[] { p }, Ids(onEleventh.GetProperty("bestSelling")));
    }

    [Fact]
    public async Task Anonymous_users_cannot_read_it()
    {
        var resp = await Anonymous.GetAsync($"/api/dashboard/top-products?{Range()}");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
