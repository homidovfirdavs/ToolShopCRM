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
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
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

        var totalPaid = await _db.Payments
            .Where(x => x.OrderId == order.Id && x.Status == PaymentStatus.Completed)
            .SumAsync(x => (decimal?)x.Amount) ?? 0;
        var outstanding = Math.Max(0, order.TotalAmount - totalPaid);

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

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Cashier")]
    public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var payment = await _paymentService.CreateAsync(
                request.OrderId, request.Amount, request.Method,
                request.ReferenceNumber, request.Notes, CurrentUserId!);

            var order = await _db.Orders.AsNoTracking().FirstAsync(o => o.Id == payment.OrderId);

            await _auditService.LogAsync(CurrentUserId, "Payment.Create", "Payment", payment.Id.ToString(),
                $"Payment {payment.PaymentNumber} of {payment.Amount} ({payment.Method}) received for order {order.OrderNumber}.");

            return CreatedAtAction(nameof(GetById), new { id = payment.Id }, await ToResponseAsync(payment, order));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Order not found." });
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

        var customerQuery = _db.Customers.AsNoTracking().Where(c => c.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            customerQuery = customerQuery.Where(c =>
                c.Name.ToLower().Contains(term) || c.Phone.Contains(term));
        }

        var customers = await customerQuery.ToListAsync();

        var debtList = new List<object>();
        foreach (var customer in customers)
        {
            var debt = await _paymentService.GetCustomerDebtAsync(customer.Id);
            if (debt > 0)
            {
                                var outstandingOrderCount = await _db.Orders
                    .Where(o => o.CustomerId == customer.Id && o.Status == OrderStatus.Completed)
                    .CountAsync(o => (_db.Payments
                        .Where(p => p.OrderId == o.Id && p.Status == PaymentStatus.Completed)
                        .Sum(p => (decimal?)p.Amount) ?? 0m) < o.TotalAmount);
                var lastPurchase = await _db.Orders
                    .Where(o => o.CustomerId == customer.Id && o.Status == OrderStatus.Completed)
                    .OrderByDescending(o => o.OrderDate)
                    .Select(o => (DateTime?)o.OrderDate)
                    .FirstOrDefaultAsync();

                debtList.Add(new
                {
                    customerId = customer.Id,
                    customerName = customer.Name,
                    phone = customer.Phone,
                    outstandingOrders = outstandingOrderCount,
                    totalDebt = debt,
                    lastPurchase
                });
            }
        }

        var ordered = debtList
            .OrderByDescending(d => (decimal)d.GetType().GetProperty("totalDebt")!.GetValue(d)!)
            .ToList();

        var totalItems = ordered.Count;
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new { items = pageItems, page, pageSize, totalItems, totalPages });
    }


}