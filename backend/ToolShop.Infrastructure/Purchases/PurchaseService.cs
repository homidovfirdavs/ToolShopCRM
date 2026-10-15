using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Purchases;

public class PurchaseAlreadyConfirmedException : Exception
{
    public PurchaseAlreadyConfirmedException() : base("Purchase has already been confirmed.") { }
}

public class PurchaseNotDraftException : Exception
{
    public PurchaseNotDraftException(string action)
        : base($"Only draft purchases can be {action}.") { }
}

public class PurchaseService
{
    private readonly AppDbContext _db;

    public PurchaseService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Purchase> ConfirmAsync(int purchaseId, string userId)
    {
        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            IDbContextTransaction? transaction = null;

            try
            {
                transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var purchase = await _db.Purchases
                    .Include(p => p.Items)
                    .FirstOrDefaultAsync(p => p.Id == purchaseId);

                if (purchase is null)
                {
                    throw new KeyNotFoundException("Purchase not found.");
                }

                if (purchase.Status != PurchaseStatus.Draft)
                {
                    throw new PurchaseAlreadyConfirmedException();
                }

                if (purchase.Items.Count == 0)
                {
                    throw new InvalidOperationException("Purchase must have at least one item to confirm.");
                }

                // Recalculate totals server-side, never trust stale client values.
                decimal subtotal = 0;
                foreach (var item in purchase.Items)
                {
                    item.LineTotal = item.Quantity * item.UnitCost;
                    subtotal += item.LineTotal;
                }

                purchase.Subtotal = subtotal;
                purchase.TotalAmount = subtotal - purchase.DiscountAmount;

                purchase.Status = PurchaseStatus.Confirmed;
                purchase.ConfirmedByUserId = userId;
                purchase.ConfirmedAt = DateTime.UtcNow;

                foreach (var item in purchase.Items)
                {
                    var movement = new InventoryMovement
                    {
                        ProductId = item.ProductId,
                        MovementType = InventoryMovementType.Purchase,
                        Quantity = item.Quantity,
                        ReferenceType = "Purchase",
                        ReferenceId = purchase.Id,
                        Reason = $"Purchase {purchase.PurchaseNumber}",
                        CreatedByUserId = userId,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.InventoryMovements.Add(movement);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return purchase;
            }
            catch (Exception ex) when (ToolShop.Infrastructure.Persistence.SerializationFailure.IsRetryable(ex) && attempt < maxRetries)
            {
                await SafeRollbackAsync(transaction);
                _db.ChangeTracker.Clear(); // drop half-applied entities so a retry cannot duplicate them
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync();
                }
            }
        }

        throw new InvalidOperationException("Could not confirm purchase due to concurrent updates. Please try again.");
    }

    public async Task CancelDraftAsync(int purchaseId)
    {
        var purchase = await _db.Purchases.FirstOrDefaultAsync(p => p.Id == purchaseId);

        if (purchase is null)
        {
            throw new KeyNotFoundException("Purchase not found.");
        }

        if (purchase.Status != PurchaseStatus.Draft)
        {
            throw new PurchaseNotDraftException("cancelled");
        }

        purchase.Status = PurchaseStatus.Cancelled;
        purchase.CancelledAt = DateTime.UtcNow;

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