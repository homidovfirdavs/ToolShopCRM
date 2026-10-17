using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Idempotency;
using ToolShop.Infrastructure.Inventory;
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
        : base("Only completed or partially returned orders can be returned.")
    {
        Status = status;
    }
}

/// <summary>A return request that breaks a business rule (bad line, too many units, refund above the limit...).</summary>
public class InvalidReturnException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>One line of a return: how many units come back and how many of them are damaged / not sellable.</summary>
public record ReturnLineInput(int OrderItemId, int Quantity, int DamagedQuantity);

/// <summary>Lines == null/empty means "return everything that is still left" (all units sellable).</summary>
public record ReturnInput(string Reason, List<ReturnLineInput>? Lines, PaymentMethod? RefundMethod, decimal? RefundAmount);

/// <summary>Outcome of one return operation.</summary>
/// <param name="Replayed">True when this is the result stored by an earlier request with the same Idempotency-Key (nothing was changed now).</param>
public record OrderReturnResult(Order Order, OrderReturn Return, int ItemCount, decimal RefundedAmount, bool Replayed = false);

/// <summary>Outcome of completing a sale. <paramref name="Replayed"/>: the sale was completed by an earlier request with the same Idempotency-Key.</summary>
public record OrderCompleteResult(Order Order, bool Replayed);

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
    /// F3a - cost of one unit of each product's CURRENT stock (null = unknown), see <see cref="MovingAverageCost"/>.
    /// Runs inside the sale's serializable transaction, before the sale's own movements exist.
    /// </summary>
    private async Task<Dictionary<int, decimal?>> CalculateSaleCostsAsync(List<int> productIds)
    {
        var movements = await _db.InventoryMovements.AsNoTracking()
            .Where(m => productIds.Contains(m.ProductId))
            .OrderBy(m => m.Id)
            .Select(m => new { m.ProductId, m.MovementType, m.Quantity, m.ReferenceType, m.ReferenceId })
            .ToListAsync();

        // Purchases: real unit cost per (purchase, product), with the purchase-level discount spread over the lines pro rata.
        var purchaseIds = movements
            .Where(m => m.MovementType == InventoryMovementType.Purchase && m.ReferenceId != null)
            .Select(m => m.ReferenceId!.Value).Distinct().ToList();

        var purchaseLines = await _db.PurchaseItems.AsNoTracking()
            .Where(pi => purchaseIds.Contains(pi.PurchaseId) && productIds.Contains(pi.ProductId)
                         && pi.Purchase.Status == PurchaseStatus.Confirmed)
            .Select(pi => new { pi.PurchaseId, pi.ProductId, pi.Quantity, pi.UnitCost, pi.Purchase.Subtotal, pi.Purchase.TotalAmount })
            .ToListAsync();

        var purchaseCost = purchaseLines
            .GroupBy(l => (l.PurchaseId, l.ProductId))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var quantity = g.Sum(l => l.Quantity);
                    var gross = g.Sum(l => l.Quantity * l.UnitCost);
                    var factor = g.First().Subtotal > 0 ? g.First().TotalAmount / g.First().Subtotal : 1m;
                    return quantity == 0 ? (decimal?)null : gross * factor / quantity;
                });

        // Returned units go back to stock at the cost they were sold for (null = that sale's cost was unknown).
        var returnOrderIds = movements
            .Where(m => m.MovementType == InventoryMovementType.SaleReturn && m.ReferenceId != null)
            .Select(m => m.ReferenceId!.Value).Distinct().ToList();

        var soldLines = await _db.OrderItems.AsNoTracking()
            .Where(i => returnOrderIds.Contains(i.OrderId) && productIds.Contains(i.ProductId))
            .Select(i => new { i.OrderId, i.ProductId, i.Quantity, i.CostAtSale })
            .ToListAsync();

        var saleCost = soldLines
            .GroupBy(l => (l.OrderId, l.ProductId))
            .ToDictionary(
                g => g.Key,
                g => g.Any(l => l.CostAtSale is null)
                    ? (decimal?)null
                    : g.Sum(l => l.Quantity * l.CostAtSale!.Value) / g.Sum(l => l.Quantity));

        var result = new Dictionary<int, decimal?>();
        foreach (var productId in productIds)
        {
            var events = new List<CostEvent>();
            foreach (var m in movements.Where(m => m.ProductId == productId))
            {
                switch (m.MovementType)
                {
                    case InventoryMovementType.Purchase:
                        events.Add(m.ReferenceId is { } pid && purchaseCost.TryGetValue((pid, productId), out var pc) && pc is { } pcost
                            ? new CostEvent(CostEventKind.KnownIn, m.Quantity, pcost)
                            : new CostEvent(CostEventKind.UnknownIn, m.Quantity));
                        break;

                    case InventoryMovementType.SaleReturn:
                        events.Add(m.ReferenceId is { } oid && saleCost.TryGetValue((oid, productId), out var sc) && sc is { } scost
                            ? new CostEvent(CostEventKind.KnownIn, m.Quantity, scost)
                            : new CostEvent(CostEventKind.UnknownIn, m.Quantity));
                        break;

                    case InventoryMovementType.StockIn:
                    case InventoryMovementType.AdjustmentIncrease:
                        events.Add(new CostEvent(CostEventKind.UnknownIn, m.Quantity));
                        break;

                    default: // StockOut, AdjustmentDecrease, Sale, PurchaseReturn
                        events.Add(new CostEvent(CostEventKind.Out, m.Quantity));
                        break;
                }
            }

            result[productId] = MovingAverageCost.Compute(events);
        }

        return result;
    }

    /// <summary>
/// Completes a draft order. When <paramref name="initialPayment"/> is supplied it is recorded in the SAME
/// serializable transaction (stock deduction + completion + payment are all-or-nothing).
/// Business rule: a walk-in order (CustomerId == null) can only be completed with a payment covering the full total.
/// Idempotent per (user, <paramref name="idempotencyKey"/>): the same request sent again (double click, retry after a lost
/// answer) returns the order the first request completed and changes nothing. The key is stored in the SAME transaction, so a
/// rejected sale (stock, walk-in rule, 5xx) leaves its key free for a corrected retry.
/// </summary>
public async Task<OrderCompleteResult> CompleteAsync(
        int orderId, string userId, string idempotencyKey, InitialPaymentInput? initialPayment = null)
    {
        // Same-key losers retry once into the replay; extra headroom for ordinary 40001s.
        const int maxRetries = 12; // parallel sales of one product conflict on SERIALIZABLE; each loser retries
        var requestHash = OrderRequestHash.ForComplete(orderId, initialPayment);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            IDbContextTransaction? transaction = null;

            try
            {
                transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                // 1) Claim the key (or find the stored result of an identical earlier request).
                var (idem, replayed) = await IdempotencyGate.BeginAsync(
                    _db, IdempotencyGate.CompleteScope, userId, idempotencyKey, requestHash, nameof(Order));

                if (replayed)
                {
                    var stored = await _db.Orders.AsNoTracking()
                        .Include(o => o.Items).ThenInclude(i => i.Product)
                        .FirstOrDefaultAsync(o => o.Id == idem.ResultEntityId)
                        ?? throw new InvalidOperationException("Idempotency record points to an order that does not exist.");

                    await transaction.CommitAsync();
                    return new OrderCompleteResult(stored, Replayed: true);
                }

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

                // F3a - COGS cost snapshot = MOVING WEIGHTED AVERAGE cost of the stock at the moment of sale, replayed from the
                // inventory movements (purchases at their real cost incl. the purchase discount, returned units at their sale cost).
                // Units whose cost was never recorded (stock-in / adjustment up) make the cost UNKNOWN: CostAtSale = null - never an
                // estimate and never 0. The value is written once and never recalculated, so history stays stable.
                var saleCosts = await CalculateSaleCostsAsync(productIds);

                // Recalculate totals server-side.
                decimal subtotal = 0;
                foreach (var item in order.Items)
                {
                    item.LineTotal = (item.Quantity * item.UnitPrice) - item.DiscountAmount;
                    subtotal += item.LineTotal;

                    item.CostAtSale = saleCosts.GetValueOrDefault(item.ProductId);
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
                // The sale date is the moment of completion (a draft may have been held for days); revenue and COGS are
                // bucketed by OrderDate while payments use their own date.
                order.OrderDate = order.CompletedAt.Value;

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

                idem.ResultEntityId = order.Id;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return new OrderCompleteResult(order, Replayed: false);
            }
            catch (Exception ex) when (IdempotencyGate.IsRetryable(ex) && attempt < maxRetries)
            {
                await SafeRollbackAsync(transaction);
                _db.ChangeTracker.Clear(); // drop half-applied entities before retrying
                // Small random pause so the losers do not collide again in lockstep.
                await Task.Delay(Random.Shared.Next(10, 40) * attempt);
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
        // One atomic conditional UPDATE: a concurrent Complete can never be overwritten by a stale "Cancelled".
        var cancelledAt = (DateTime?)DateTime.UtcNow;
        var changed = await _db.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.Draft)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Cancelled)
                .SetProperty(o => o.CancelledAt, cancelledAt));

        if (changed == 1)
        {
            return;
        }

        var exists = await _db.Orders.AsNoTracking().AnyAsync(o => o.Id == orderId);
        if (!exists)
        {
            throw new KeyNotFoundException("Order not found.");
        }

        throw new OrderNotDraftException("cancelled");
    }

    /// <summary>
    /// Returns goods of a sold order - the whole order or only some lines/units (TASK C2). One serializable transaction:
    ///  - ledger: one immutable OrderReturn (+ items) dated NOW (UTC). Revenue is reduced in THIS period, the sale stays where it was,
    ///  - stock: a SaleReturn movement only for the RESTOCKED (sellable) units; damaged units never go back to stock,
    ///  - money: nothing is edited or flipped - the refund is a number on the return, payments keep their history,
    ///  - order: Completed -> PartiallyReturned -> Returned (when no unit is left).
    /// Returned value = line value after the pro-rata order-discount allocation; the LAST return of a line (and of the order)
    /// takes the rounding remainder, so all returns together add up to exactly what was sold.
    /// Refund = money actually paid back; limited to what the customer overpaid; defaults to that limit.
    /// </summary>
    /// <remarks>
    /// Idempotent per (user, <paramref name="idempotencyKey"/>): the same return sent again returns the stored one and creates
    /// no second ledger row, refund or stock movement. A genuinely NEW partial return needs a NEW key.
    /// </remarks>
    public async Task<OrderReturnResult> ReturnAsync(int orderId, string userId, string idempotencyKey, ReturnInput input)
    {
        const int maxRetries = 5;
        var requestHash = OrderRequestHash.ForReturn(orderId, input);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            IDbContextTransaction? transaction = null;

            try
            {
                transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                // Claim the key first (before the return number is drawn), or find the stored result of an identical earlier request.
                var (idem, replayed) = await IdempotencyGate.BeginAsync(
                    _db, IdempotencyGate.ReturnScope, userId, idempotencyKey, requestHash, nameof(OrderReturn));

                if (replayed)
                {
                    var storedReturn = await _db.OrderReturns.AsNoTracking()
                        .Include(r => r.Items)
                        .FirstOrDefaultAsync(r => r.Id == idem.ResultEntityId)
                        ?? throw new InvalidOperationException("Idempotency record points to a return that does not exist.");
                    var storedOrder = await _db.Orders.AsNoTracking()
                        .Include(o => o.Items)
                        .FirstAsync(o => o.Id == storedReturn.OrderId);

                    await transaction.CommitAsync();
                    return new OrderReturnResult(storedOrder, storedReturn, storedReturn.Items.Count, storedReturn.RefundAmount, Replayed: true);
                }

                var order = await _db.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order is null)
                {
                    throw new KeyNotFoundException("Order not found.");
                }

                if (order.Status != OrderStatus.Completed && order.Status != OrderStatus.PartiallyReturned)
                {
                    throw new OrderNotReturnableException(order.Status);
                }

                var now = DateTime.UtcNow;
                var reason = input.Reason.Trim();

                // ---- what was already returned (ledger) ----
                var priorItems = await _db.OrderReturnItems.AsNoTracking()
                    .Where(i => i.OrderReturn.OrderId == orderId)
                    .Select(i => new { i.OrderItemId, i.Quantity, i.NetValue })
                    .ToListAsync();
                var returnedQty = priorItems.GroupBy(i => i.OrderItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
                var returnedNet = priorItems.GroupBy(i => i.OrderItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.NetValue));
                var balanceBefore = await OrderBalance.LoadAsync(_db, order.Id, order.TotalAmount);

                // ---- requested lines ----
                var lines = input.Lines is { Count: > 0 }
                    ? input.Lines
                    : order.Items
                        .Where(i => i.Quantity - returnedQty.GetValueOrDefault(i.Id) > 0)
                        .Select(i => new ReturnLineInput(i.Id, i.Quantity - returnedQty.GetValueOrDefault(i.Id), 0))
                        .ToList();

                if (lines.Count == 0)
                {
                    throw new InvalidReturnException("NOTHING_TO_RETURN", "There is nothing left to return on this order.");
                }

                if (lines.GroupBy(l => l.OrderItemId).Any(g => g.Count() > 1))
                {
                    throw new InvalidReturnException("DUPLICATE_LINE", "Each order line may appear only once in a return.");
                }

                var ledger = new OrderReturn
                {
                    ReturnNumber = await ReturnNumberGenerator.GenerateAsync(_db),
                    OrderId = order.Id,
                    ReturnedAt = now,
                    ReturnedByUserId = userId,
                    Reason = reason
                };

                var thisQty = new Dictionary<int, int>();
                foreach (var line in lines)
                {
                    var item = order.Items.FirstOrDefault(i => i.Id == line.OrderItemId)
                        ?? throw new InvalidReturnException("INVALID_ITEM", $"Order line {line.OrderItemId} does not belong to this order.");

                    var remaining = item.Quantity - returnedQty.GetValueOrDefault(item.Id);
                    if (line.Quantity < 1 || line.Quantity > remaining)
                    {
                        throw new InvalidReturnException("QUANTITY_EXCEEDS_REMAINING",
                            $"Line {item.Id}: cannot return {line.Quantity}, only {remaining} unit(s) left.");
                    }

                    if (line.DamagedQuantity < 0 || line.DamagedQuantity > line.Quantity)
                    {
                        throw new InvalidReturnException("INVALID_DAMAGED_QUANTITY",
                            $"Line {item.Id}: damaged quantity must be between 0 and {line.Quantity}.");
                    }

                    // Line value after order-discount allocation (pro-rata); the last return of the line takes the remainder.
                    var lineNet = order.Subtotal > 0
                        ? Math.Round(item.LineTotal * order.TotalAmount / order.Subtotal, 2, MidpointRounding.AwayFromZero)
                        : 0m;
                    var net = line.Quantity == remaining
                        ? lineNet - returnedNet.GetValueOrDefault(item.Id)
                        : Math.Round(lineNet * line.Quantity / item.Quantity, 2, MidpointRounding.AwayFromZero);

                    thisQty[item.Id] = line.Quantity;
                    ledger.Items.Add(new OrderReturnItem
                    {
                        OrderItemId = item.Id,
                        ProductId = item.ProductId,
                        Quantity = line.Quantity,
                        RestockedQuantity = line.Quantity - line.DamagedQuantity,
                        WrittenOffQuantity = line.DamagedQuantity,
                        NetValue = Math.Max(0m, net),
                        CostAtSaleSnapshot = item.CostAtSale
                    });
                }

                var fullyReturned = order.Items.All(i =>
                    i.Quantity - returnedQty.GetValueOrDefault(i.Id) - thisQty.GetValueOrDefault(i.Id) == 0);

                // The order is exhausted: all returns together must equal the order total to the cent.
                if (fullyReturned)
                {
                    var diff = (order.TotalAmount - balanceBefore.ReturnedValue) - ledger.Items.Sum(i => i.NetValue);
                    var last = ledger.Items[^1];
                    last.NetValue = Math.Max(0m, last.NetValue + diff);
                }

                ledger.GoodsValue = ledger.Items.Sum(i => i.NetValue);

                // ---- cost: restocked units leave COGS, written-off units stay in COGS (memo only). Unknown cost -> null, never 0 ----
                decimal? costReversed = 0m, writeOff = 0m;
                foreach (var i in ledger.Items)
                {
                    if (i.RestockedQuantity > 0)
                    {
                        costReversed = i.CostAtSaleSnapshot is null || costReversed is null
                            ? null : costReversed + i.RestockedQuantity * i.CostAtSaleSnapshot;
                    }
                    if (i.WrittenOffQuantity > 0)
                    {
                        writeOff = i.CostAtSaleSnapshot is null || writeOff is null
                            ? null : writeOff + i.WrittenOffQuantity * i.CostAtSaleSnapshot;
                    }
                }
                ledger.CostReversed = costReversed;
                ledger.WriteOffCost = writeOff;

                // ---- refund: limited to what the customer has overpaid after this return ----
                var overpaid = Math.Max(0m,
                    balanceBefore.NetPaid - (balanceBefore.NetTotal - ledger.GoodsValue));
                var refund = Math.Round(input.RefundAmount ?? overpaid, 2, MidpointRounding.AwayFromZero);
                if (refund < 0 || refund > overpaid)
                {
                    throw new InvalidReturnException("REFUND_EXCEEDS_LIMIT",
                        $"Refund must be between 0 and {overpaid} (what the customer has overpaid).");
                }

                ledger.RefundAmount = refund;
                if (refund > 0)
                {
                    ledger.RefundMethod = input.RefundMethod
                        ?? await _db.Payments.AsNoTracking()
                            .Where(p => p.OrderId == order.Id && p.Status == PaymentStatus.Completed)
                            .OrderByDescending(p => p.CreatedAt)
                            .Select(p => (PaymentMethod?)p.Method)
                            .FirstOrDefaultAsync()
                        ?? PaymentMethod.Cash;
                }

                // ---- stock: only sellable units go back ----
                foreach (var i in ledger.Items.Where(i => i.RestockedQuantity > 0))
                {
                    _db.InventoryMovements.Add(new InventoryMovement
                    {
                        ProductId = i.ProductId,
                        MovementType = InventoryMovementType.SaleReturn,
                        Quantity = i.RestockedQuantity,
                        ReferenceType = "Order",
                        ReferenceId = order.Id,
                        Reason = $"Return {order.OrderNumber} ({ledger.ReturnNumber})",
                        Notes = reason,
                        CreatedByUserId = userId,
                        CreatedAt = now
                    });
                }

                order.Status = fullyReturned ? OrderStatus.Returned : OrderStatus.PartiallyReturned;
                _db.OrderReturns.Add(ledger);

                await _db.SaveChangesAsync();

                idem.ResultEntityId = ledger.Id; // the ledger id exists only after the first save
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();

                return new OrderReturnResult(order, ledger, ledger.Items.Count, ledger.RefundAmount);
            }
            catch (Exception ex) when (IdempotencyGate.IsRetryable(ex) && attempt < maxRetries)
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