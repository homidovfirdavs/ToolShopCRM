using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Inventory;

public class InsufficientStockException(int available, int requested)
    : Exception($"Insufficient stock. Available: {available}, requested: {requested}.")
{
    public int Available { get; } = available;
    public int Requested { get; } = requested;
}

public class InventoryService
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

    public InventoryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetCurrentStockAsync(int productId)
    {
        var movements = await _db.InventoryMovements
            .AsNoTracking()
            .Where(m => m.ProductId == productId)
            .Select(m => new { m.MovementType, m.Quantity })
            .ToListAsync();

        return CalculateStock(movements.Select(m => (m.MovementType, m.Quantity)));
    }

    public async Task<Dictionary<int, int>> GetCurrentStockBulkAsync(IEnumerable<int> productIds)
    {
        var ids = productIds.ToList();

        var movements = await _db.InventoryMovements
            .AsNoTracking()
            .Where(m => ids.Contains(m.ProductId))
            .Select(m => new { m.ProductId, m.MovementType, m.Quantity })
            .ToListAsync();

        return ids.ToDictionary(
            id => id,
            id => CalculateStock(movements
                .Where(m => m.ProductId == id)
                .Select(m => (m.MovementType, m.Quantity))));
    }

    private static int CalculateStock(IEnumerable<(InventoryMovementType Type, int Quantity)> movements)
    {
        var stock = 0;
        foreach (var (type, quantity) in movements)
        {
            if (IncreaseTypes.Contains(type))
            {
                stock += quantity;
            }
            else if (DecreaseTypes.Contains(type))
            {
                stock -= quantity;
            }
        }
        return stock;
    }

    public async Task<InventoryMovement> CreateStockInAsync(
        int productId, int quantity, string reason, string? notes, string userId)
    {
        return await ExecuteMutationAsync(productId, InventoryMovementType.StockIn, quantity, reason, notes, userId);
    }

    public async Task<InventoryMovement> CreateStockOutAsync(
        int productId, int quantity, string reason, string? notes, string userId)
    {
        return await ExecuteMutationAsync(productId, InventoryMovementType.StockOut, quantity, reason, notes, userId);
    }

    public async Task<InventoryMovement> CreateAdjustmentAsync(
        int productId, InventoryMovementType type, int quantity, string reason, string? notes, string userId)
    {
        if (type != InventoryMovementType.AdjustmentIncrease && type != InventoryMovementType.AdjustmentDecrease)
        {
            throw new ArgumentException("Adjustment type must be AdjustmentIncrease or AdjustmentDecrease.");
        }

        return await ExecuteMutationAsync(productId, type, quantity, reason, notes, userId);
    }

    private async Task<InventoryMovement> ExecuteMutationAsync(
        int productId, InventoryMovementType type, int quantity, string reason, string? notes, string userId)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity must be greater than zero.");
        }

        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            IDbContextTransaction? transaction = null;

            try
            {
                transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var currentStock = await GetCurrentStockAsync(productId);

                if (DecreaseTypes.Contains(type) && currentStock < quantity)
                {
                    throw new InsufficientStockException(currentStock, quantity);
                }

                var movement = new InventoryMovement
                {
                    ProductId = productId,
                    MovementType = type,
                    Quantity = quantity,
                    Reason = reason,
                    Notes = notes,
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                _db.InventoryMovements.Add(movement);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return movement;
            }
            catch (DbUpdateException) when (attempt < maxRetries)
            {
                await SafeRollbackAsync(transaction);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "40001" && attempt < maxRetries)
            {
                // serialization_failure — another concurrent transaction won the race; retry.
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

        throw new InvalidOperationException("Could not complete stock operation due to concurrent updates. Please try again.");
    }

    private static async Task SafeRollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            try { await transaction.RollbackAsync(); } catch { /* already rolled back */ }
        }
    }
}