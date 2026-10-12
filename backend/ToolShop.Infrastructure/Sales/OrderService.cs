using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Payments;
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

public class OrderNotReturnableException : Exception
{
    public OrderStatus Status { get; }

    public OrderNotReturnableException(OrderStatus status)
        : base("Only completed orders can be returned.")
    {
        Status = status;
    }
}

/// <summary>Outcome of a full-order return.</summary>
public record OrderReturnResult(Order Order, int ItemCount, decimal RefundedAmount);

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

/// <summary>Initial payment taken at the moment of sale (POS). Saved in the same transaction as the completion.</summary>
public record InitialPaymentInput(decimal Amount, PaymentMethod Method, string? ReferenceNumber, string? Notes);

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

    /// <summary>
/// Completes a draft order. When <paramref name="initialPayment"/> is supplied it is recorded in the SAME
/// serializable transaction (stock deduction + completion + payment are all-or-nothing).
/// Business rule: a walk-in order (CustomerId == null) can only be completed with a payment covering the full total.
/// </summary>
public async Task<Order> CompleteAsync(int orderId, string userId, InitialPaymentInput? initialPayment = null)
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

                // TASK 09 — COGS cost snapshot strategy:
                // 1) Use the UnitCost from the most recently CONFIRMED purchase of this product.
                // 2) If no purchase history exists, fall back to the product's current PurchasePrice.
                // This value never changes again after being written (historical profit stays stable).
                var latestPurchaseCosts = await _db.PurchaseItems
                    .AsNoTracking()
                    .Where(pi => productIds.Contains(pi.ProductId) && pi.Purchase.Status == PurchaseStatus.Confirmed)
                    .GroupBy(pi => pi.ProductId)
                    .Select(g => new
                    {
                        ProductId = g.Key,
                        UnitCost = g.OrderByDescending(pi => pi.Purchase.ConfirmedAt).Select(pi => pi.UnitCost).First()
                    })
                    .ToDictionaryAsync(x => x.ProductId, x => x.UnitCost);

                // Recalculate totals server-side.
                decimal subtotal = 0;
                foreach (var item in order.Items)
                {
                    item.LineTotal = (item.Quantity * item.UnitPrice) - item.DiscountAmount;
                    subtotal += item.LineTotal;

                    item.CostAtSale = latestPurchaseCosts.TryGetValue(item.ProductId, out var cost)
                        ? cost
                        : item.Product.PurchasePrice;
                }

                order.Subtotal = subtotal;
                order.TotalAmount = Math.Max(0, subtotal - order.DiscountAmount);

                // Payment rules (validated on the server, against the server-calculated total).
                if (initialPayment is not null && initialPayment.Amount <= 0)
                {
                    throw new ArgumentException("Initial payment amount must be greater than zero.");
                }

                var initialAmount = initialPayment?.Amount ?? 0m;
                if (initialAmount > order.TotalAmount)
                {
                    throw new PaymentOverpaymentException(order.TotalAmount, initialAmount);
                }

                if (order.CustomerId is null && order.TotalAmount > 0 && initialAmount < order.TotalAmount)
                {
                    throw new WalkInCreditNotAllowedException();
                }

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

                if (initialPayment is not null)
                {
                    _db.Payments.Add(new Payment
                    {
                        PaymentNumber = await PaymentNumberGenerator.GenerateAsync(_db),
                        OrderId = order.Id,
                        CustomerId = order.CustomerId,
                        Amount = initialPayment.Amount,
                        Method = initialPayment.Method,
                        Status = PaymentStatus.Completed,
                        ReferenceNumber = initialPayment.ReferenceNumber,
                        Notes = initialPayment.Notes,
                        ReceivedByUserId = userId,
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
                _db.ChangeTracker.Clear(); // drop half-applied entities before retrying
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

    /// <summary>
    /// Returns a COMPLETED order in full (partial returns are not supported). One serializable transaction:
    ///  - stock: one SaleReturn InventoryMovement per line (stock always comes from movements),
    ///  - money: every valid (Completed) payment of the order becomes Refunded (kept for history, never deleted),
    ///  - order: Completed -> Returned. CostAtSale, prices and discounts are NOT touched.
    /// A Returned order drops out of revenue, COGS, receivables and debts because all of them only count Completed orders.
    /// </summary>
    public async Task<OrderReturnResult> ReturnAsync(int orderId, string userId, string reason)
    {
        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            IDbContextTransaction? transaction = null;

            try
            {
                transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var order = await _db.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order is null)
                {
                    throw new KeyNotFoundException("Order not found.");
                }

                if (order.Status != OrderStatus.Completed)
                {
                    throw new OrderNotReturnableException(order.Status);
                }

                var now = DateTime.UtcNow;
                var trimmedReason = reason.Trim();

                foreach (var item in order.Items)
                {
                    _db.InventoryMovements.Add(new InventoryMovement
                    {
                        ProductId = item.ProductId,
                        MovementType = InventoryMovementType.SaleReturn,
                        Quantity = item.Quantity,
                        ReferenceType = "Order",
                        ReferenceId = order.Id,
                        Reason = $"Return {order.OrderNumber}",
                        Notes = trimmedReason,
                        CreatedByUserId = userId,
                        CreatedAt = now
                    });
                }

                var validPayments = await _db.Payments
                    .Where(p => p.OrderId == order.Id && p.Status == PaymentStatus.Completed)
                    .ToListAsync();

                var refunded = 0m;
                foreach (var payment in validPayments)
                {
                    refunded += payment.Amount;
                    // For a Refunded payment the Voided* columns record when/who/why the money was given back.
                    payment.Status = PaymentStatus.Refunded;
                    payment.VoidedAt = now;
                    payment.VoidedByUserId = userId;
                    payment.VoidReason = $"Order returned: {trimmedReason}";
                }

                order.Status = OrderStatus.Returned;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return new OrderReturnResult(order, order.Items.Count, refunded);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "40001" && attempt < maxRetries)
            {
                await SafeRollbackAsync(transaction);
                _db.ChangeTracker.Clear();
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync();
                }
            }
        }

        throw new InvalidOperationException("Could not return the order due to concurrent updates. Please try again.");
    }

    private static async Task SafeRollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            try { await transaction.RollbackAsync(); } catch { /* already rolled back */ }
        }
    }
}