using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ToolShop.Domain.Entities.Idempotency;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Persistence;
using ToolShop.Infrastructure.Sales;

namespace ToolShop.Infrastructure.Payments;

public class PaymentOverpaymentException(decimal outstanding, decimal requested)
    : Exception($"Payment exceeds outstanding balance. Outstanding: {outstanding}, requested: {requested}.")
{
    public decimal Outstanding { get; } = outstanding;
    public decimal Requested { get; } = requested;
}

public class OrderNotEligibleForPaymentException(string reason) : Exception(reason);

public class WalkInCreditNotAllowedException()
    : Exception("Walk-in sale must be fully paid. Partial payment or credit requires an identified customer.");

/// <summary>The Idempotency-Key was already used for a DIFFERENT payment request (other order, amount, method...).</summary>
public class IdempotencyKeyReusedException()
    : Exception("This Idempotency-Key was already used for a different payment request.");

/// <summary>Result of creating a payment: the payment, and whether it is the one stored by an earlier identical request.</summary>
public record PaymentCreateResult(Payment Payment, bool Replayed);

public class PaymentAlreadyVoidedException() : Exception("Payment has already been voided.");

public class VoidWouldCreateUnresolvedWalkInDebtException()
    : Exception("Cannot void this payment: it would leave an outstanding balance on a walk-in order with no customer to bill.");

public class VoidWouldDropBelowRefundedException(decimal refunded)
    : Exception($"Cannot void this payment: the customer has already been refunded {refunded}, which the remaining payments would not cover.")
{
    public decimal Refunded { get; } = refunded;
}

public class PaymentService
{
    private readonly AppDbContext _db;

    public PaymentService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<decimal> GetOrderOutstandingBalanceAsync(int orderId)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return 0;

        // After returns: total - returned goods - (paid - refunded). Never stored.
        return (await OrderBalance.LoadAsync(_db, orderId, order.TotalAmount)).Outstanding;
    }

    public async Task<decimal> GetCustomerDebtAsync(int customerId)
    {
        // Per sold order: outstanding = max(0, total - returned goods - (valid payments - refunds)). Debt is never stored.
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId && SalesFilters.Receivable.Contains(o.Status))
            .Select(o => new { o.Id, o.TotalAmount })
            .ToListAsync();

        var balances = await OrderBalance.LoadManyAsync(_db, orders.Select(o => (o.Id, o.TotalAmount)).ToList());
        return balances.Values.Sum(b => b.Outstanding);
    }

    public const string CreateScope = "payment.create";
    private const int IdempotencyRetentionDays = 30;

    /// <summary>
    /// Records a payment exactly once per (user, Idempotency-Key). Sending the same request again with the same key (double
    /// click, retry after a timeout, two devices racing) returns the payment stored the first time and creates nothing.
    /// The key is stored in the SAME serializable transaction as the payment: a rejected request (overpayment, walk-in rule,
    /// 5xx) rolls both back, so its key stays free for a corrected retry. The key is claimed BEFORE the payment number is drawn,
    /// so a losing duplicate never consumes a number.
    /// </summary>
    public async Task<PaymentCreateResult> CreateAsync(
        int orderId, decimal amount, PaymentMethod method, string? referenceNumber, string? notes, string userId,
        string idempotencyKey)
    {
        if (amount <= 0)
        {
            throw new ArgumentException("Payment amount must be greater than zero.");
        }

        var requestHash = PaymentRequestHash.Compute(orderId, amount, method, referenceNumber, notes);

        // Losers of a same-key race retry once (the winner's stored key is then visible); extra headroom for other 40001s.
        const int maxRetries = 5;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            IDbContextTransaction? transaction = null;

            try
            {
                transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                // 1) Already done by this user with this key?
                var idem = await _db.IdempotencyRecords.FirstOrDefaultAsync(r =>
                    r.Scope == CreateScope && r.UserId == userId && r.Key == idempotencyKey);

                if (idem is not null && idem.ExpiresAt > DateTime.UtcNow)
                {
                    if (idem.RequestHash != requestHash)
                    {
                        throw new IdempotencyKeyReusedException();
                    }

                    var stored = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == idem.ResultEntityId)
                        ?? throw new InvalidOperationException("Idempotency record points to a payment that does not exist.");

                    await transaction.CommitAsync();
                    return new PaymentCreateResult(stored, Replayed: true);
                }

                if (idem is not null)
                {
                    // Expired: forget it so the same key can be used again.
                    _db.IdempotencyRecords.Remove(idem);
                    await _db.SaveChangesAsync();
                }

                // 2) Claim the key first. A concurrent request with the same key blocks here until we commit, then fails
                //    (unique violation / serialization failure) and retries into step 1.
                var now = DateTime.UtcNow;
                idem = new IdempotencyRecord
                {
                    Scope = CreateScope,
                    UserId = userId,
                    Key = idempotencyKey,
                    RequestHash = requestHash,
                    ResultEntityType = nameof(Payment),
                    ResultEntityId = 0,
                    CreatedAt = now,
                    ExpiresAt = now.AddDays(IdempotencyRetentionDays)
                };
                _db.IdempotencyRecords.Add(idem);
                await _db.SaveChangesAsync();

                // 3) Business rules (unchanged).
                var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
                if (order is null)
                {
                    throw new KeyNotFoundException("Order not found.");
                }

                if (order.Status != OrderStatus.Completed && order.Status != OrderStatus.PartiallyReturned)
                {
                    throw new OrderNotEligibleForPaymentException(
                        "Payments can only be recorded against completed orders.");
                }

                var outstanding = (await OrderBalance.LoadAsync(_db, orderId, order.TotalAmount)).Outstanding;

                if (amount > outstanding)
                {
                    throw new PaymentOverpaymentException(outstanding, amount);
                }

                // Walk-in credit rule: if order has no customer, this payment must fully settle it.
                if (order.CustomerId is null && amount < outstanding)
                {
                    throw new WalkInCreditNotAllowedException();
                }

                var paymentNumber = await PaymentNumberGenerator.GenerateAsync(_db);

                var payment = new Payment
                {
                    PaymentNumber = paymentNumber,
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    Amount = amount,
                    Method = method,
                    Status = PaymentStatus.Completed,
                    ReferenceNumber = referenceNumber,
                    Notes = notes,
                    ReceivedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                _db.Payments.Add(payment);
                await _db.SaveChangesAsync();

                idem.ResultEntityId = payment.Id;
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();

                return new PaymentCreateResult(payment, Replayed: false);
            }
            catch (Exception ex) when ((ToolShop.Infrastructure.Persistence.SerializationFailure.IsRetryable(ex)
                                        || ToolShop.Infrastructure.Persistence.IdempotencyKeyConflict.Is(ex))
                                       && attempt < maxRetries)
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

        throw new InvalidOperationException("Could not record payment due to concurrent updates. Please try again.");
    }

    public async Task<Payment> VoidAsync(int paymentId, string reason, string userId)
    {
        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            IDbContextTransaction? transaction = null;

            try
            {
                transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var payment = await _db.Payments
                    .Include(p => p.Order)
                    .FirstOrDefaultAsync(p => p.Id == paymentId);

                if (payment is null)
                {
                    throw new KeyNotFoundException("Payment not found.");
                }

                if (payment.Status != PaymentStatus.Completed)
                {
                    throw new PaymentAlreadyVoidedException();
                }

                var balance = await OrderBalance.LoadAsync(_db, payment.OrderId, payment.Order.TotalAmount);

                // Money already paid back must stay covered by real payments: voiding below the refunded amount is rejected.
                if (balance.Paid - payment.Amount < balance.Refunded)
                {
                    throw new VoidWouldDropBelowRefundedException(balance.Refunded);
                }

                // Walk-in safety: voiding must not leave an unresolved debt with no customer.
                if (payment.Order.CustomerId is null)
                {
                    var wouldBeOutstanding = balance with { Paid = balance.Paid - payment.Amount };

                    if (wouldBeOutstanding.Outstanding > 0)
                    {
                        throw new VoidWouldCreateUnresolvedWalkInDebtException();
                    }
                }

                payment.Status = PaymentStatus.Voided;
                payment.VoidedAt = DateTime.UtcNow;
                payment.VoidedByUserId = userId;
                payment.VoidReason = reason;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return payment;
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

        throw new InvalidOperationException("Could not void payment due to concurrent updates. Please try again.");
    }

    private static async Task SafeRollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            try { await transaction.RollbackAsync(); } catch { /* already rolled back */ }
        }
    }
}