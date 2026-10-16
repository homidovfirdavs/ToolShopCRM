using ToolShop.Domain.Entities.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Catalog;
using ToolShop.API.DTOs.Payments;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Audit;
using ToolShop.Infrastructure.Payments;
using ToolShop.Infrastructure.Sales;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize(Roles = "Admin,Manager,Cashier")]
public class PaymentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PaymentService _paymentService;
    private readonly AuditService _auditService;
    private const int MaxPageSize = 100;

    public PaymentsController(AppDbContext db, PaymentService paymentService, AuditService auditService)
    {
        _db = db;
        _paymentService = paymentService;
        _auditService = auditService;
    }

    private string? CurrentUserId =>
        User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

    private static OrderPaymentState ResolveState(decimal total, decimal paid)
    {
        if (paid <= 0) return OrderPaymentState.Unpaid;
        if (paid < total) return OrderPaymentState.PartiallyPaid;
        return OrderPaymentState.Paid;
    }

    private async Task<PaymentResponse> ToResponseAsync(Payment p, Order order)
    {
        var userIds = new List<string> { p.ReceivedByUserId };
        if (p.VoidedByUserId is not null) userIds.Add(p.VoidedByUserId);

        var users = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName }).ToListAsync();
        var userLookup = users.ToDictionary(u => u.Id, u => u.UserName ?? "Unknown");

        var balance = await ToolShop.Infrastructure.Sales.OrderBalance.LoadAsync(_db, order.Id, order.TotalAmount);
        var totalPaid = balance.Paid;
        var outstanding = balance.Outstanding;

        string customerName = "Walk-in Customer";
        if (order.CustomerId.HasValue)
        {
            var customer = await _db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == order.CustomerId.Value);
            if (customer is not null) customerName = customer.Name;
        }

        return new PaymentResponse
        {
            Id = p.Id,
            PaymentNumber = p.PaymentNumber,
            OrderId = p.OrderId,
            OrderNumber = order.OrderNumber,
            CustomerId = p.CustomerId,
            CustomerName = customerName,
            Amount = p.Amount,
            Method = p.Method,
            Status = p.Status,
            ReferenceNumber = p.ReferenceNumber,
            Notes = p.Notes,
            ReceivedByUsername = userLookup.GetValueOrDefault(p.ReceivedByUserId, "Unknown"),
            CreatedAt = p.CreatedAt,
            VoidedAt = p.VoidedAt,
            VoidedByUsername = p.VoidedByUserId is not null
                ? userLookup.GetValueOrDefault(p.VoidedByUserId, "Unknown")
                : null,
            VoidReason = p.VoidReason,
            OrderTotal = order.TotalAmount,
            TotalPaid = totalPaid,
            OutstandingBalance = outstanding,
            PaymentState = ResolveState(order.TotalAmount, totalPaid)
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? orderId,
        [FromQuery] int? customerId,
        [FromQuery] PaymentMethod? method,
        [FromQuery] PaymentStatus? status,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var query = _db.Payments.Include(p => p.Order).Include(p => p.Customer)
            .AsNoTracking().AsQueryable();

        if (orderId.HasValue) query = query.Where(p => p.OrderId == orderId.Value);
        if (customerId.HasValue) query = query.Where(p => p.CustomerId == customerId.Value);
        if (method.HasValue) query = query.Where(p => p.Method == method.Value);
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        if (fromDate.HasValue) query = query.Where(p => p.CreatedAt >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(p => p.CreatedAt <= toDate.Value);

        query = query.OrderByDescending(p => p.CreatedAt);

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var payments = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var userIds = payments.Select(p => p.ReceivedByUserId).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName }).ToListAsync();
        var userLookup = users.ToDictionary(u => u.Id, u => u.UserName ?? "Unknown");

        var items = payments.Select(p => new PaymentListItemResponse
        {
            Id = p.Id,
            PaymentNumber = p.PaymentNumber,
            CreatedAt = p.CreatedAt,
            OrderNumber = p.Order.OrderNumber,
            CustomerName = p.Customer?.Name ?? "Walk-in Customer",
            Amount = p.Amount,
            Method = p.Method,
            Status = p.Status,
            ReceivedByUsername = userLookup.GetValueOrDefault(p.ReceivedByUserId, "Unknown")
        }).ToList();

        return Ok(new PagedResult<PaymentListItemResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var payment = await _db.Payments.Include(p => p.Order).FirstOrDefaultAsync(p => p.Id == id);
        if (payment is null) return NotFound(new { message = "Payment not found." });

        return Ok(await ToResponseAsync(payment, payment.Order));
    }

    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string ReplayHeader = "Idempotent-Replay";

    private static readonly System.Text.RegularExpressions.Regex IdempotencyKeyFormat =
        new("^[A-Za-z0-9_-]{16,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Records a payment once per Idempotency-Key header (a client-generated id for one user intention, 16-64 chars of
    /// A-Z a-z 0-9 _ -). The same key with the same request returns the first payment again (HTTP 201 plus header
    /// "Idempotent-Replay: true", nothing new is written or audited); the same key with a different request is 409.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Cashier")]
    public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var key = Request.Headers[IdempotencyKeyHeader].ToString().Trim();
        if (string.IsNullOrEmpty(key))
        {
            return BadRequest(new
            {
                code = "IDEMPOTENCY_KEY_REQUIRED",
                message = "The Idempotency-Key header is required for payments."
            });
        }

        if (!IdempotencyKeyFormat.IsMatch(key))
        {
            return BadRequest(new
            {
                code = "IDEMPOTENCY_KEY_INVALID",
                message = "The Idempotency-Key must be 16-64 characters: letters, digits, '-' or '_'."
            });
        }

        try
        {
            var result = await _paymentService.CreateAsync(
                request.OrderId, request.Amount, request.Method,
                request.ReferenceNumber, request.Notes, CurrentUserId!, key);
            var payment = result.Payment;

            var order = await _db.Orders.AsNoTracking().FirstAsync(o => o.Id == payment.OrderId);

            if (result.Replayed)
            {
                Response.Headers[ReplayHeader] = "true";
            }
            else
            {
                await _auditService.LogAsync(CurrentUserId, "Payment.Create", "Payment", payment.Id.ToString(),
                    $"Payment {payment.PaymentNumber} of {payment.Amount} ({payment.Method}) received for order {order.OrderNumber}.");
            }

            return CreatedAtAction(nameof(GetById), new { id = payment.Id }, await ToResponseAsync(payment, order));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Order not found." });
        }
        catch (IdempotencyKeyReusedException ex)
        {
            return Conflict(new { code = "IDEMPOTENCY_KEY_REUSED", message = ex.Message });
        }
        catch (OrderNotEligibleForPaymentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (PaymentOverpaymentException ex)
        {
            return Conflict(new { message = "Payment exceeds outstanding balance.", outstanding = ex.Outstanding, requested = ex.Requested });
        }
        catch (WalkInCreditNotAllowedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/void")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Void(int id, [FromBody] VoidPaymentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var payment = await _paymentService.VoidAsync(id, request.Reason, CurrentUserId!);
            var order = await _db.Orders.AsNoTracking().FirstAsync(o => o.Id == payment.OrderId);

            await _auditService.LogAsync(CurrentUserId, "Payment.Void", "Payment", payment.Id.ToString(),
                $"Payment {payment.PaymentNumber} voided. Reason: {request.Reason}");

            return Ok(await ToResponseAsync(payment, order));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Payment not found." });
        }
        catch (PaymentAlreadyVoidedException)
        {
            return Conflict(new { message = "Payment has already been voided." });
        }
        catch (VoidWouldCreateUnresolvedWalkInDebtException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (VoidWouldDropBelowRefundedException ex)
        {
            return Conflict(new { message = ex.Message, code = "VOID_BELOW_REFUNDED", refunded = ex.Refunded });
        }
    }
        [HttpGet("~/api/debts")]
    public async Task<IActionResult> GetDebts(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var customerQuery = _db.Customers.AsNoTracking().AsQueryable(); // inactive customers keep their debt (still owed)

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            customerQuery = customerQuery.Where(c =>
                c.Name.ToLower().Contains(term) || c.Phone.Contains(term));
        }

        // Set-based: one query for matching customers, one for their completed orders with paid sums.
        // debt = Order.Total - valid (Completed) Payments, never stored.
        var customers = await customerQuery
            .Select(c => new { c.Id, c.Name, c.Phone, c.IsActive })
            .ToListAsync();
        var customerIds = customers.Select(c => c.Id).ToList();

        var debtOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.CustomerId != null && customerIds.Contains(o.CustomerId.Value)
                        && SalesFilters.Receivable.Contains(o.Status))
            .Select(o => new { o.Id, CustomerId = o.CustomerId!.Value, o.OrderDate, o.TotalAmount })
            .ToListAsync();
        // After returns: total - returned goods - (paid - refunded). Same formula as Finance / Dashboard / customer debt.
        var debtBalances = await OrderBalance.LoadManyAsync(_db, debtOrders.Select(o => (o.Id, o.TotalAmount)).ToList());
        var orderRows = debtOrders
            .Select(o => new { o.CustomerId, o.OrderDate, Outstanding = debtBalances[o.Id].Outstanding })
            .ToList();

        var byCustomer = orderRows.GroupBy(r => r.CustomerId).ToDictionary(g => g.Key, g => g.ToList());

        var ordered = customers
            .Select(c =>
            {
                var rows = byCustomer.GetValueOrDefault(c.Id) ?? [];
                var debt = rows.Sum(r => r.Outstanding);
                return new
                {
                    customerId = c.Id,
                    customerName = c.Name,
                    isActive = c.IsActive,
                    phone = c.Phone,
                    outstandingOrders = rows.Count(r => r.Outstanding > 0m),
                    totalDebt = debt,
                    lastPurchase = rows.Count == 0 ? (DateTime?)null : rows.Max(r => r.OrderDate)
                };
            })
            .Where(d => d.totalDebt > 0m)
            .OrderByDescending(d => d.totalDebt)
            .ToList();

        var totalItems = ordered.Count;
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        // Reconciliation only (not a debt line): completed walk-in orders (no customer) that still have an
        // outstanding balance. Dashboard/Finance receivables include them; this list cannot (no customer to bill).
        var walkInOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == null && SalesFilters.Receivable.Contains(o.Status))
            .Select(o => new { o.Id, o.TotalAmount })
            .ToListAsync();
        var walkInBalances = await OrderBalance.LoadManyAsync(_db, walkInOrders.Select(o => (o.Id, o.TotalAmount)).ToList());
        var walkInOutstanding = walkInBalances.Values
            .Select(b => b.Outstanding)
            .Where(x => x > 0m)
            .ToList();

        return Ok(new
        {
            items = pageItems, page, pageSize, totalItems, totalPages,
            walkInUnresolvedDebt = walkInOutstanding.Sum(),
            walkInUnresolvedOrderCount = walkInOutstanding.Count
        });
    }

    /// <summary>
    /// Historical walk-in orders (no customer) that still have an outstanding balance. Read-only,
    /// for the owner's separate decision. This endpoint never modifies, voids or deletes anything.
    /// </summary>
    [HttpGet("~/api/debts/walk-in-exceptions")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> GetWalkInExceptions()
    {
        var walkInRows = await _db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == null && SalesFilters.Receivable.Contains(o.Status))
            .Select(o => new { o.Id, o.OrderNumber, o.OrderDate, Total = o.TotalAmount })
            .ToListAsync();
        var walkInBalances = await OrderBalance.LoadManyAsync(_db, walkInRows.Select(o => (o.Id, o.Total)).ToList());

        var items = walkInRows
            .Where(r => walkInBalances[r.Id].Outstanding > 0m)
            .OrderBy(r => r.OrderDate)
            .Select(r => new
            {
                orderId = r.Id,
                orderNumber = r.OrderNumber,
                orderDate = r.OrderDate,
                total = r.Total,
                paid = walkInBalances[r.Id].NetPaid,
                outstanding = walkInBalances[r.Id].Outstanding
            })
            .ToList();

        return Ok(new { items, totalOutstanding = items.Sum(i => i.outstanding), count = items.Count });
    }
}
