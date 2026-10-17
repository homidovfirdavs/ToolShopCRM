using System.Net;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// F3a - CostAtSale is the MOVING WEIGHTED AVERAGE cost of the stock at the moment of sale, replayed from the inventory movements:
/// purchases at their real cost (purchase discount spread pro rata), returned units at their sale cost, units with an unrecorded
/// cost make the cost UNKNOWN (null, never 0, never an estimate). History is never rewritten.
/// </summary>
[Collection("db")]
public class CogsMovingAverageTests(ApiFactory factory) : TestBase(factory)
{
    private async Task<(int OrderId, int ItemId)> SellAsync(int productId, int quantity)
    {
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 0m, Item(productId, quantity));
        await (await CompleteAsync(id, total)).ShouldBeAsync(HttpStatusCode.OK);
        var itemId = await Factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == id).Select(i => i.Id).SingleAsync());
        return (id, itemId);
    }

    private Task<decimal?> CostAsync(int orderId) =>
        Factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == orderId).Select(i => i.CostAtSale).SingleAsync());

    private Task<HttpResponseMessage> ReturnAsync(int orderId, int itemId, int quantity, int damaged = 0) =>
        PostWithKeyAsync(Admin, $"/api/orders/{orderId}/return",
            new { reason = "Customer returned goods", items = new[] { new { orderItemId = itemId, quantity, damagedQuantity = damaged } } },
            NewKey());

    // ---------- average ----------

    [Fact]
    public async Task Two_purchases_at_different_costs_give_their_weighted_average()
    {
        var product = await SeedProductAsync(stock: 0);
        await SeedConfirmedPurchaseAsync(product, 10, 50m);
        await SeedConfirmedPurchaseAsync(product, 10, 70m);

        var (order, _) = await SellAsync(product, 1);
        Assert.Equal(60m, await CostAsync(order));
    }

    [Fact]
    public async Task Selling_does_not_change_the_average_and_a_later_purchase_is_weighted_with_the_rest()
    {
        var product = await SeedProductAsync(stock: 0);
        await SeedConfirmedPurchaseAsync(product, 10, 50m);
        await SeedConfirmedPurchaseAsync(product, 10, 70m);

        Assert.Equal(60m, await CostAsync((await SellAsync(product, 5)).OrderId));
        Assert.Equal(60m, await CostAsync((await SellAsync(product, 1)).OrderId));   // 14 left at 60

        await SeedConfirmedPurchaseAsync(product, 5, 90m);                            // (14x60 + 5x90) / 19
        Assert.Equal(67.89m, await CostAsync((await SellAsync(product, 1)).OrderId));
    }

    [Fact]
    public async Task The_purchase_level_discount_is_spread_over_the_cost()
    {
        var product = await SeedProductAsync(stock: 0);
        await SeedConfirmedPurchaseAsync(product, 10, 100m, discount: 100m);          // 1000 - 100 = 900 -> 90 per unit

        Assert.Equal(90m, await CostAsync((await SellAsync(product, 1)).OrderId));
    }

    [Fact]
    public async Task Opening_stock_seeded_through_a_purchase_costs_its_purchase_price()
    {
        var product = await SeedProductAsync(purchasePrice: 60m, stock: 20);

        Assert.Equal(60m, await CostAsync((await SellAsync(product, 2)).OrderId));
    }

    // ---------- returns ----------

    [Fact]
    public async Task Restocked_units_come_back_at_the_cost_they_were_sold_for()
    {
        var product = await SeedProductAsync(stock: 0);
        await SeedConfirmedPurchaseAsync(product, 10, 50m);
        var (firstOrder, firstItem) = await SellAsync(product, 4);                    // cost 50, 6 left at 50
        Assert.Equal(50m, await CostAsync(firstOrder));
        await SeedConfirmedPurchaseAsync(product, 10, 70m);                           // (6x50 + 10x70) / 16 = 62.5

        await (await ReturnAsync(firstOrder, firstItem, 2)).ShouldBeAsync(HttpStatusCode.OK);   // 2 back at 50
        // (16 x 62.5 + 2 x 50) / 18 = 61.11
        Assert.Equal(61.11m, await CostAsync((await SellAsync(product, 1)).OrderId));
    }

    [Fact]
    public async Task Damaged_returned_units_do_not_go_back_to_stock_so_the_average_is_unchanged()
    {
        var product = await SeedProductAsync(stock: 0);
        await SeedConfirmedPurchaseAsync(product, 10, 50m);
        var (firstOrder, firstItem) = await SellAsync(product, 4);
        await SeedConfirmedPurchaseAsync(product, 10, 70m);                           // 62.5

        await (await ReturnAsync(firstOrder, firstItem, 2, damaged: 2)).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(62.5m, await CostAsync((await SellAsync(product, 1)).OrderId));
    }

    // ---------- unknown cost ----------

    [Fact]
    public async Task Stock_with_an_unrecorded_cost_is_sold_with_CostAtSale_null_not_zero_and_not_the_product_price()
    {
        var product = await SeedProductAsync(purchasePrice: 60m, stock: 10, uncostedStock: true);

        var (order, _) = await SellAsync(product, 1);
        Assert.Null(await CostAsync(order));

        var today = DateTime.UtcNow.Date;
        var summary = await (await Admin.GetAsync($"/api/finance/summary?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}"))
            .ReadJsonAsync();
        Assert.Equal(1, summary.Int("unknownCostItemsCount"));                       // reported as N/A, never as a profit
    }

    [Fact]
    public async Task Mixing_unrecorded_stock_with_a_purchase_keeps_the_cost_unknown()
    {
        var product = await SeedProductAsync(stock: 5, uncostedStock: true);
        await SeedConfirmedPurchaseAsync(product, 5, 50m);

        Assert.Null(await CostAsync((await SellAsync(product, 1)).OrderId));
    }

    [Fact]
    public async Task A_confirmed_purchase_at_cost_zero_is_a_real_zero_not_unknown()
    {
        var product = await SeedProductAsync(stock: 0);
        await SeedConfirmedPurchaseAsync(product, 10, 0m);

        Assert.Equal(0m, await CostAsync((await SellAsync(product, 1)).OrderId));
    }

    [Fact]
    public async Task A_purchase_movement_without_a_purchase_document_is_unknown()
    {
        var product = await SeedProductAsync(stock: 0);
        await Factory.WithDbAsync(async db =>
        {
            db.InventoryMovements.Add(new InventoryMovement
            {
                ProductId = product,
                MovementType = InventoryMovementType.Purchase,
                Quantity = 5,
                Reason = "orphan purchase movement",
                CreatedByUserId = Factory.AdminUserId
            });
            await db.SaveChangesAsync();
        });

        Assert.Null(await CostAsync((await SellAsync(product, 1)).OrderId));
    }

    [Fact]
    public async Task Unknown_cost_ends_when_the_stock_is_sold_out_and_a_new_purchase_starts_clean()
    {
        var product = await SeedProductAsync(stock: 2, uncostedStock: true);
        Assert.Null(await CostAsync((await SellAsync(product, 2)).OrderId));          // sold out

        await SeedConfirmedPurchaseAsync(product, 10, 50m);
        Assert.Equal(50m, await CostAsync((await SellAsync(product, 1)).OrderId));
    }

    // ---------- history ----------

    [Fact]
    public async Task A_later_purchase_never_rewrites_the_cost_of_an_earlier_sale()
    {
        var product = await SeedProductAsync(stock: 0);
        await SeedConfirmedPurchaseAsync(product, 10, 50m);
        var (firstOrder, _) = await SellAsync(product, 1);

        await SeedConfirmedPurchaseAsync(product, 10, 90m);                           // (9x50 + 10x90) / 19 = 71.05
        var (secondOrder, _) = await SellAsync(product, 1);

        Assert.Equal(50m, await CostAsync(firstOrder));
        Assert.Equal(71.05m, await CostAsync(secondOrder));
    }

    // ---------- concurrency ----------

    [Fact]
    public async Task Ten_sales_at_the_same_time_all_get_the_same_average_cost_and_stock_is_exact()
    {
        var product = await SeedProductAsync(purchasePrice: 50m, stock: 20);
        var orders = new List<(int Id, decimal Total)>();
        for (var i = 0; i < 10; i++)
        {
            orders.Add(await CreateOrderAsync(await SeedCustomerAsync(), 0m, Item(product, 1)));
        }

        var responses = await Task.WhenAll(orders.Select(o => CompleteAsync(o.Id, o.Total)));
        foreach (var r in responses) await r.ShouldBeAsync(HttpStatusCode.OK);

        foreach (var o in orders) Assert.Equal(50m, await CostAsync(o.Id));
        Assert.Equal(10, await StockAsync(product));
    }
}
