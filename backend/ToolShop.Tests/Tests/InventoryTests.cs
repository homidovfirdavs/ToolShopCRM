using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>Stock is never stored: it is always the sum of InventoryMovement rows.</summary>
[Collection("db")]
public class InventoryTests(ApiFactory factory) : TestBase(factory)
{
    private Task<HttpResponseMessage> StockIn(int productId, int qty, HttpClient? c = null, string reason = "test") =>
        (c ?? Admin).PostAsJsonAsync("/api/inventory/stock-in", new { productId, quantity = qty, reason });

    private Task<HttpResponseMessage> StockOut(int productId, int qty, HttpClient? c = null, string reason = "test") =>
        (c ?? Admin).PostAsJsonAsync("/api/inventory/stock-out", new { productId, quantity = qty, reason });

    private async Task AddMovementAsync(int productId, InventoryMovementType type, int qty) =>
        await Factory.WithDbAsync(async db =>
        {
            db.InventoryMovements.Add(new InventoryMovement
            {
                ProductId = productId,
                MovementType = type,
                Quantity = qty,
                Reason = "test",
                CreatedByUserId = Factory.AdminUserId
            });
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task Stock_is_the_signed_sum_of_all_movement_types()
    {
        var product = await SeedProductAsync(stock: 10);              // StockIn +10

        await (await StockOut(product, 3)).ShouldBeAsync(HttpStatusCode.OK);   // -3  -> 7
        await (await Admin.PostAsJsonAsync("/api/inventory/adjustment",
            new { productId = product, type = "AdjustmentIncrease", quantity = 2, reason = "count" }))
            .ShouldBeAsync(HttpStatusCode.OK);                                  // +2  -> 9
        await (await Admin.PostAsJsonAsync("/api/inventory/adjustment",
            new { productId = product, type = "AdjustmentDecrease", quantity = 1, reason = "damaged" }))
            .ShouldBeAsync(HttpStatusCode.OK);                                  // -1  -> 8
        await (await StockIn(product, 4)).ShouldBeAsync(HttpStatusCode.OK);    // +4  -> 12
        Assert.Equal(12, await StockAsync(product));

        await AddMovementAsync(product, InventoryMovementType.Purchase, 5);        // +5
        await AddMovementAsync(product, InventoryMovementType.SaleReturn, 1);      // +1
        await AddMovementAsync(product, InventoryMovementType.PurchaseReturn, 2);  // -2
        await AddMovementAsync(product, InventoryMovementType.Sale, 3);            // -3
        Assert.Equal(13, await StockAsync(product));
    }

    [Fact]
    public async Task Stock_out_beyond_available_is_rejected_and_changes_nothing()
    {
        var product = await SeedProductAsync(stock: 5);
        var movementsBefore = await MovementCountAsync();

        var resp = await StockOut(product, 6);
        await resp.ShouldBeAsync(HttpStatusCode.Conflict);
        var json = await resp.ReadJsonAsync();
        Assert.Equal(5, json.Int("available"));
        Assert.Equal(6, json.Int("requested"));

        Assert.Equal(5, await StockAsync(product));
        Assert.Equal(movementsBefore, await MovementCountAsync());
    }

    [Fact]
    public async Task Stock_out_of_exactly_the_available_quantity_empties_the_product()
    {
        var product = await SeedProductAsync(stock: 4, minimumStock: 1);

        await (await StockOut(product, 4)).ShouldBeAsync(HttpStatusCode.OK);

        var resp = await Admin.GetAsync($"/api/inventory/stock/{product}");
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        var json = await resp.ReadJsonAsync();
        Assert.Equal(0, json.Int("currentStock"));
        Assert.Equal("OutOfStock", json.Str("status"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Zero_or_negative_quantity_is_rejected(int qty)
    {
        var product = await SeedProductAsync(stock: 5);

        await (await StockIn(product, qty)).ShouldBeAsync(HttpStatusCode.BadRequest);
        await (await StockOut(product, qty)).ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal(5, await StockAsync(product));
    }

    [Fact]
    public async Task Reason_is_required()
    {
        var product = await SeedProductAsync(stock: 5);
        await (await StockIn(product, 1, reason: "")).ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal(5, await StockAsync(product));
    }

    [Fact]
    public async Task Inactive_or_unknown_products_cannot_receive_or_lose_stock()
    {
        var inactive = await SeedProductAsync(stock: 3, active: false);
        await (await StockIn(inactive, 1)).ShouldBeAsync(HttpStatusCode.BadRequest);
        await (await StockIn(999_999, 1)).ShouldBeAsync(HttpStatusCode.NotFound);
        await (await StockOut(999_999, 1)).ShouldBeAsync(HttpStatusCode.NotFound);
        Assert.Equal(3, await StockAsync(inactive));
    }

    [Fact]
    public async Task Roles_control_who_can_change_stock()
    {
        var product = await SeedProductAsync(stock: 5);

        await (await StockIn(product, 1, Warehouse)).ShouldBeAsync(HttpStatusCode.OK);
        await (await StockIn(product, 1, Manager)).ShouldBeAsync(HttpStatusCode.OK);
        await (await StockIn(product, 1, Cashier)).ShouldBeAsync(HttpStatusCode.Forbidden);
        await (await StockIn(product, 1, Anonymous)).ShouldBeAsync(HttpStatusCode.Unauthorized);
        Assert.Equal(7, await StockAsync(product));
    }

    [Fact]
    public async Task Drafts_do_not_touch_stock_and_cancelling_a_draft_keeps_it()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();
        var movements = await MovementCountAsync();

        var (orderId, _) = await CreateOrderAsync(customer, 0m, Item(product, 3));
        Assert.Equal(10, await StockAsync(product));

        await (await Admin.PostAsync($"/api/orders/{orderId}/cancel", null)).ShouldBeAsync(HttpStatusCode.NoContent);
        Assert.Equal(10, await StockAsync(product));
        Assert.Equal(movements, await MovementCountAsync());
    }

    [Fact]
    public async Task Completing_a_sale_deducts_exactly_the_sold_quantity_with_a_Sale_movement()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 10);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateOrderAsync(customer, 0m, Item(product, 4));

        await (await CompleteAsync(orderId)).ShouldBeAsync(HttpStatusCode.OK);

        Assert.Equal(6, await StockAsync(product));
        var sale = await Factory.WithDbAsync(db => db.InventoryMovements
            .Where(m => m.ProductId == product && m.MovementType == InventoryMovementType.Sale)
            .ToListAsync());
        var movement = Assert.Single(sale);
        Assert.Equal(4, movement.Quantity);
        Assert.Equal("Order", movement.ReferenceType);
        Assert.Equal(orderId, movement.ReferenceId);

        // Completing twice must not deduct twice.
        await (await CompleteAsync(orderId)).ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal(6, await StockAsync(product));
    }

    [Fact]
    public async Task Selling_more_than_the_stock_is_rejected_with_a_shortage_report()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 3);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateOrderAsync(customer, 0m, Item(product, 5));
        var movements = await MovementCountAsync();

        var resp = await CompleteAsync(orderId);
        await resp.ShouldBeAsync(HttpStatusCode.Conflict);
        var json = await resp.ReadJsonAsync();
        Assert.Equal("INSUFFICIENT_STOCK", json.Str("code"));
        var shortage = json.GetProperty("shortages")[0];
        Assert.Equal(product, shortage.Int("productId"));
        Assert.Equal(3, shortage.Int("available"));
        Assert.Equal(5, shortage.Int("requested"));

        Assert.Equal(OrderStatus.Draft, await OrderStatusAsync(orderId));
        Assert.Equal(3, await StockAsync(product));
        Assert.Equal(movements, await MovementCountAsync());
    }

    [Fact]
    public async Task A_multi_item_sale_is_all_or_nothing()
    {
        var plenty = await SeedProductAsync(salePrice: 100m, stock: 10);
        var scarce = await SeedProductAsync(salePrice: 100m, stock: 1);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateOrderAsync(customer, 0m, Item(plenty, 2), Item(scarce, 5));

        await (await CompleteAsync(orderId)).ShouldBeAsync(HttpStatusCode.Conflict);

        Assert.Equal(10, await StockAsync(plenty)); // nothing was deducted for the item that DID have stock
        Assert.Equal(1, await StockAsync(scarce));
        Assert.Equal(OrderStatus.Draft, await OrderStatusAsync(orderId));
    }

    [Fact]
    public async Task A_product_deactivated_after_the_draft_cannot_be_sold()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 5);
        var customer = await SeedCustomerAsync();
        var (orderId, _) = await CreateOrderAsync(customer, 0m, Item(product, 1));

        await Factory.WithDbAsync(async db =>
        {
            var p = await db.Products.FirstAsync(x => x.Id == product);
            p.IsActive = false;
            await db.SaveChangesAsync();
        });

        await (await CompleteAsync(orderId)).ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal(5, await StockAsync(product));
    }

    [Fact]
    public async Task Two_simultaneous_sales_cannot_oversell_the_last_units()
    {
        var product = await SeedProductAsync(salePrice: 100m, stock: 5);
        var customer = await SeedCustomerAsync();
        var (order1, _) = await CreateOrderAsync(customer, 0m, Item(product, 4));
        var (order2, _) = await CreateOrderAsync(customer, 0m, Item(product, 4));

        var results = await Task.WhenAll(CompleteAsync(order1), CompleteAsync(order2));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.DoesNotContain(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(1, await StockAsync(product)); // 5 - 4, never negative
    }

    [Fact]
    public async Task Parallel_stock_outs_never_drive_stock_below_zero()
    {
        var product = await SeedProductAsync(stock: 5);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => StockOut(product, 1)));

        Assert.All(responses.Select(r => r.StatusCode), s => Assert.Contains(s,
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.InternalServerError }));
        var ok = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.InRange(ok, 1, 5);

        var stock = await StockAsync(product);
        Assert.True(stock >= 0, $"Stock went negative: {stock}");
        Assert.Equal(5 - ok, stock); // every 200 is really recorded, nothing else is
    }
}
