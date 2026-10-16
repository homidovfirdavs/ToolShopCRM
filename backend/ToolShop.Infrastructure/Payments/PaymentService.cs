using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    public async Task<Payment> CreateAsync(
        int orderId, decimal amount, PaymentMethod method, string? referenceNumber, string? notes, string userId)
    {
        if (amount <= 0)
        {
            throw new ArgumentException("Payment amount must be greater than zero.");
        }

        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            IDbContextTransaction? transaction = null;

            try
            {
                transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

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