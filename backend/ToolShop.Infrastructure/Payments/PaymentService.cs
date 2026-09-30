using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Persistence;

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

        var totalPaid = await _db.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        return Math.Max(0, order.TotalAmount - totalPaid);
    }

    public async Task<decimal> GetCustomerDebtAsync(int customerId)
    {
        var orderIds = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Completed)
            .Select(o => o.Id)
            .ToListAsync();

        decimal totalDebt = 0;
        foreach (var orderId in orderIds)
        {
            totalDebt += await GetOrderOutstandingBalanceAsync(orderId);
        }

        return totalDebt;
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

                if (order.Status != OrderStatus.Completed)
                {
                    throw new OrderNotEligibleForPaymentException(
                        "Payments can only be recorded against completed orders.");
                }

                var totalPaidSoFar = await _db.Payments
                    .Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Completed)
                    .SumAsync(p => (decimal?)p.Amount) ?? 0;

                var outstanding = Math.Max(0, order.TotalAmount - totalPaidSoFar);

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

                // Walk-in safety: voiding must not leave an unresolved debt with no customer.
                if (payment.Order.CustomerId is null)
                {
                    var totalPaidExcludingThis = await _db.Payments
                        .Where(p => p.OrderId == payment.OrderId &&
                                    p.Status == PaymentStatus.Completed &&
                                    p.Id != payment.Id)
                        .SumAsync(p => (decimal?)p.Amount) ?? 0;

                    var wouldBeOutstanding = Math.Max(0, payment.Order.TotalAmount - totalPaidExcludingThis);

                    if (wouldBeOutstanding > 0)
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