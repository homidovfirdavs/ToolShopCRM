using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Sales;

public class OrderAlreadyCompletedException : Exception
{
    public OrderAlreadyCompletedException() : base("Order has already been completed or is not a draft.") { }
}

public class OrderNotDraftException : Exception
{
    public OrderNotDraftException(string action)
        : base($"Only draft orders can be {action}.") { }
}

public class InsufficientStockForSaleException : Exception
{
    public List<(int ProductId, string ProductName, int Available, int Requested)> Shortages { get; }

    public InsufficientStockForSaleException(
        List<(int ProductId, string ProductName, int Available, int Requested)> shortages)
        : base("Insufficient stock for one or more products.")
    {
        Shortages = shortages;
    }
}

public class OrderService
{
    private readonly AppDbContext _db;

    private static readonly HashSet<InventoryMovementType> IncreaseTypes =
    [
        InventoryMovementType.StockIn,
        InventoryMovementType.AdjustmentIncrease,
        InventoryMovementType.Purchase,
        InventoryMovementType.SaleReturn
    ];

    private static readonly HashSet<InventoryMovementType> DecreaseTypes =
    [
        InventoryMovementType.StockOut,
        InventoryMovementType.AdjustmentDecrease,
        InventoryMovementType.Sale,
        InventoryMovementType.PurchaseReturn
    ];

    public OrderService(AppDbContext db)
    {
        _db = db;
    }

    private static int CalculateStock(IEnumerable<(InventoryMovementType Type, int Quantity)> movements)
    {
        var stock = 0;
        foreach (var (type, quantity) in movements)
        {
            if (IncreaseTypes.Contains(type)) stock += quantity;
            else if (DecreaseTypes.Contains(type)) stock -= quantity;
        }
        return stock;
    }

    public async Task<Order> CompleteAsync(int orderId, string userId)
    {
        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            IDbContextTransaction? transaction = null;

            try
            {
                transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var order = await _db.Orders
                    .Include(o => o.Items).ThenInclude(i => i.Product)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order is null)
                {
                    throw new KeyNotFoundException("Order not found.");
                }

                if (order.Status != OrderStatus.Draft)
                {
                    throw new OrderAlreadyCompletedException();
                }

                if (order.Items.Count == 0)
                {
                    throw new InvalidOperationException("Order must have at least one item to complete.");
                }

                // Validate every product is still active.
                var inactiveProduct = order.Items.FirstOrDefault(i => !i.Product.IsActive);
                if (inactiveProduct is not null)
                {
                    throw new InvalidOperationException(
                        $"Product '{inactiveProduct.Product.Name}' is no longer active and cannot be sold.");
                }

                // Validate stock for ALL items before mutating anything.
                var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
                var movements = await _db.InventoryMovements
                    .AsNoTracking()
                    .Where(m => productIds.Contains(m.ProductId))
                    .Select(m => new { m.ProductId, m.MovementType, m.Quantity })
                    .ToListAsync();

                var stockByProduct = productIds.ToDictionary(
                    id => id,
                    id => CalculateStock(movements
                        .Where(m => m.ProductId == id)
                        .Select(m => (m.MovementType, m.Quantity))));

                // Aggregate requested quantity per product (in case of duplicate lines).
                var requestedByProduct = order.Items
                    .GroupBy(i => i.ProductId)
                    .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

                var shortages = new List<(int, string, int, int)>();
                foreach (var (productId, requested) in requestedByProduct)
                {
                    var available = stockByProduct.GetValueOrDefault(productId, 0);
                    if (available < requested)
                    {
                        var productName = order.Items.First(i => i.ProductId == productId).Product.Name;
                        shortages.Add((productId, productName, available, requested));
                    }
                }

                if (shortages.Count > 0)
                {
                    throw new InsufficientStockForSaleException(shortages);
                }

                // Recalculate totals server-side.
                decimal subtotal = 0;
                foreach (var item in order.Items)
                {
                    item.LineTotal = (item.Quantity * item.UnitPrice) - item.DiscountAmount;
                    subtotal += item.LineTotal;
                }

                order.Subtotal = subtotal;
                order.TotalAmount = Math.Max(0, subtotal - order.DiscountAmount);

                order.Status = OrderStatus.Completed;
                order.CompletedByUserId = userId;
                order.CompletedAt = DateTime.UtcNow;

                foreach (var item in order.Items)
                {
                    _db.InventoryMovements.Add(new InventoryMovement
                    {
                        ProductId = item.ProductId,
                        MovementType = InventoryMovementType.Sale,
                        Quantity = item.Quantity,
                        ReferenceType = "Order",
                        ReferenceId = order.Id,
                        Reason = $"Sale {order.OrderNumber}",
                        CreatedByUserId = userId,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return order;
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "40001" && attempt < maxRetries)
            {
                await SafeRollbackAsync(transaction);
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync();
                }
            }
        }

        throw new InvalidOperationException("Could not complete sale due to concurrent updates. Please try again.");
    }

    public async Task CancelDraftAsync(int orderId)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);

        if (order is null)
        {
            throw new KeyNotFoundException("Order not found.");
        }

        if (order.Status != OrderStatus.Draft)
        {
            throw new OrderNotDraftException("cancelled");
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    private static async Task SafeRollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            try { await transaction.RollbackAsync(); } catch { /* already rolled back */ }
        }
    }
}