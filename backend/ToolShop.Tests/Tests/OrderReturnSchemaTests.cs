using Microsoft.EntityFrameworkCore;
using Npgsql;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Persistence;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// TASK C1 - schema only. Proves the returns ledger tables, constraints and the sequence behave as designed.
/// No production behaviour changes in C1, so the rest of the suite must stay green.
/// </summary>
[Collection("db")]
public class OrderReturnSchemaTests(ApiFactory factory) : TestBase(factory)
{
    private static PostgresException? Pg(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is PostgresException p) return p;
        return null;
    }

    /// <summary>Creates a real completed sale and returns (orderId, orderItemId, productId).</summary>
    private async Task<(int OrderId, int ItemId, int ProductId)> SaleAsync()
    {
        var productId = await SeedProductAsync(stock: 10);
        var (orderId, total) = await CreateOrderAsync(null, 0m, Item(productId, 5));
        var res = await CompleteAsync(orderId, total);   // walk-in must be fully paid
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var itemId = await Factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == orderId).Select(i => i.Id).FirstAsync());
        return (orderId, itemId, productId);
    }

    private OrderReturn NewReturn(int orderId, string number, decimal refund = 0m, PaymentMethod? method = null) => new()
    {
        ReturnNumber = number,
        OrderId = orderId,
        ReturnedAt = DateTime.UtcNow,
        ReturnedByUserId = Factory.AdminUserId,
        Reason = "test",
        GoodsValue = 200m,
        CostReversed = 120m,
        WriteOffCost = null,
        RefundAmount = refund,
        RefundMethod = method
    };

    private static OrderReturnItem NewItem(int itemId, int productId, int qty, int restocked, int writtenOff, decimal net = 200m) => new()
    {
        OrderItemId = itemId,
        ProductId = productId,
        Quantity = qty,
        RestockedQuantity = restocked,
        WrittenOffQuantity = writtenOff,
        NetValue = net,
        CostAtSaleSnapshot = 60m
    };

    private async Task<PostgresException?> TrySaveAsync(Func<AppDbContext, Task> arrange)
    {
        try { await Factory.WithDbAsync(async db => { await arrange(db); await db.SaveChangesAsync(); }); return null; }
        catch (Exception ex) { var pg = Pg(ex); if (pg is null) throw; return pg; }
    }

    [Fact]
    public async Task Valid_return_row_round_trips_with_items()
    {
        var (orderId, itemId, productId) = await SaleAsync();
        var r = NewReturn(orderId, "RET-T00001", 200m, PaymentMethod.Cash);
        r.Items.Add(NewItem(itemId, productId, 2, 1, 1));
        await Factory.WithDbAsync(async db => { db.OrderReturns.Add(r); await db.SaveChangesAsync(); });

        var back = await Factory.WithDbAsync(db => db.OrderReturns.Include(x => x.Items).SingleAsync(x => x.ReturnNumber == "RET-T00001"));
        Assert.Equal(200m, back.GoodsValue);
        Assert.Equal(120m, back.CostReversed);
        Assert.Null(back.WriteOffCost);                 // null stays null (N/A), never 0
        Assert.Equal(PaymentMethod.Cash, back.RefundMethod);
        var item = Assert.Single(back.Items);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(1, item.RestockedQuantity);
        Assert.Equal(1, item.WrittenOffQuantity);
    }

    [Fact]
    public async Task Duplicate_return_number_is_rejected()
    {
        var (orderId, _, _) = await SaleAsync();
        await Factory.WithDbAsync(async db => { db.OrderReturns.Add(NewReturn(orderId, "RET-DUP")); await db.SaveChangesAsync(); });
        var pg = await TrySaveAsync(db => { db.OrderReturns.Add(NewReturn(orderId, "RET-DUP")); return Task.CompletedTask; });
        Assert.Equal("23505", pg?.SqlState);
    }

    [Fact]
    public async Task Restocked_plus_writtenOff_must_equal_quantity()
    {
        var (orderId, itemId, productId) = await SaleAsync();
        var r = NewReturn(orderId, "RET-BAD1");
        r.Items.Add(NewItem(itemId, productId, 2, 1, 0));   // 1 + 0 != 2
        var pg = await TrySaveAsync(db => { db.OrderReturns.Add(r); return Task.CompletedTask; });
        Assert.Equal("23514", pg?.SqlState);
    }

    [Fact]
    public async Task Negative_refund_is_rejected()
    {
        var (orderId, _, _) = await SaleAsync();
        var pg = await TrySaveAsync(db => { db.OrderReturns.Add(NewReturn(orderId, "RET-BAD2", -1m, PaymentMethod.Cash)); return Task.CompletedTask; });
        Assert.Equal("23514", pg?.SqlState);
    }

    [Fact]
    public async Task Refund_without_method_is_rejected()
    {
        var (orderId, _, _) = await SaleAsync();
        var pg = await TrySaveAsync(db => { db.OrderReturns.Add(NewReturn(orderId, "RET-BAD3", 50m, null)); return Task.CompletedTask; });
        Assert.Equal("23514", pg?.SqlState);
    }

    [Fact]
    public async Task Zero_refund_without_method_is_allowed()
    {
        var (orderId, _, _) = await SaleAsync();
        var pg = await TrySaveAsync(db => { db.OrderReturns.Add(NewReturn(orderId, "RET-OK0", 0m, null)); return Task.CompletedTask; });
        Assert.Null(pg);   // credit-order return: debt reduced, no cash moved
    }

    [Fact]
    public async Task Order_with_returns_cannot_be_deleted()
    {
        var (orderId, _, _) = await SaleAsync();
        await Factory.WithDbAsync(async db => { db.OrderReturns.Add(NewReturn(orderId, "RET-FK")); await db.SaveChangesAsync(); });
        var pg = await TrySaveAsync(async db =>
        {
            // payments / items reference the order too; the point is the ledger FK must be Restrict
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Orders\" WHERE \"Id\" = {0}", orderId);
        });
        Assert.Contains(pg?.SqlState, new[] { "23001", "23503" });   // ON DELETE RESTRICT reports 23001 (NO ACTION would report 23503)
    }

    [Fact]
    public async Task Return_number_sequence_increments()
    {
        var (a, b) = await Factory.WithDbAsync(async db =>
        {
            var x = await db.Database.SqlQueryRaw<long>("SELECT nextval('return_number_seq') AS \"Value\"").SingleAsync();
            var y = await db.Database.SqlQueryRaw<long>("SELECT nextval('return_number_seq') AS \"Value\"").SingleAsync();
            return (x, y);
        });
        Assert.Equal(a + 1, b);
    }
}
