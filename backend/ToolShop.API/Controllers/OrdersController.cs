using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Catalog;
using ToolShop.API.DTOs.Sales;
using ToolShop.Domain.Authorization;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Infrastructure.Audit;
using ToolShop.Infrastructure.Payments;
using ToolShop.Infrastructure.Persistence;
using ToolShop.Infrastructure.Sales;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Roles = "Admin,Manager,Cashier")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly OrderService _orderService;
    private readonly AuditService _auditService;
    private const int MaxPageSize = 100;

    public OrdersController(AppDbContext db, OrderService orderService, AuditService auditService)
    {
        _db = db;
        _orderService = orderService;
        _auditService = auditService;
    }

    private string? CurrentUserId =>
        User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

    private async Task<OrderResponse> ToResponseAsync(Order o)
    {
        var userIds = new List<string> { o.CreatedByUserId };
        if (o.CompletedByUserId is not null) userIds.Add(o.CompletedByUserId);

        var users = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName }).ToListAsync();
        var userLookup = users.ToDictionary(u => u.Id, u => u.UserName ?? "Unknown");

        // Returns come from the immutable ledger (OrderReturns): every return has its own date, user, reason and refund.
        var returnRows = o.Status is OrderStatus.Returned or OrderStatus.PartiallyReturned
            ? await _db.OrderReturns.AsNoTracking()
                .Include(r => r.Items)
                .Where(r => r.OrderId == o.Id)
                .OrderBy(r => r.ReturnedAt).ThenBy(r => r.Id)
                .ToListAsync()
            : new List<OrderReturn>();

        var returnUserIds = returnRows.Select(r => r.ReturnedByUserId).Distinct().ToList();
        var returnUsers = await _db.Users.Where(u => returnUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName }).ToListAsync();
        var returnUserLookup = returnUsers.ToDictionary(u => u.Id, u => u.UserName ?? "Unknown");

        var returns = returnRows.Select(r => new OrderReturnResponse
        {
            Id = r.Id,
            ReturnNumber = r.ReturnNumber,
            ReturnedAt = r.ReturnedAt,
            ReturnedByUsername = returnUserLookup.GetValueOrDefault(r.ReturnedByUserId, "Unknown"),
            Reason = r.Reason,
            GoodsValue = r.GoodsValue,
            RefundAmount = r.RefundAmount,
            RefundMethod = r.RefundMethod,
            Items = r.Items.Select(ri => new OrderReturnItemResponse
            {
                OrderItemId = ri.OrderItemId,
                ProductId = ri.ProductId,
                ProductName = o.Items.FirstOrDefault(i => i.Id == ri.OrderItemId)?.Product.Name ?? string.Empty,
                Quantity = ri.Quantity,
                RestockedQuantity = ri.RestockedQuantity,
                WrittenOffQuantity = ri.WrittenOffQuantity,
                NetValue = ri.NetValue
            }).ToList()
        }).ToList();
        var lastReturn = returns.LastOrDefault();

        return new OrderResponse
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            CustomerId = o.CustomerId,
            CustomerName = o.Customer?.Name ?? "Walk-in Customer",
            Status = o.Status,
            OrderDate = o.OrderDate,
            Notes = o.Notes,
            Subtotal = o.Subtotal,
            DiscountAmount = o.DiscountAmount,
            TotalAmount = o.TotalAmount,
            Items = o.Items.Select(i => new OrderItemResponse
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.Product.Name,
                SKU = i.Product.SKU,
                Barcode = i.Product.Barcode,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                DiscountAmount = i.DiscountAmount,
                LineTotal = i.LineTotal
            }).ToList(),
            CreatedByUsername = userLookup.GetValueOrDefault(o.CreatedByUserId, "Unknown"),
            CompletedByUsername = o.CompletedByUserId is not null
                ? userLookup.GetValueOrDefault(o.CompletedByUserId, "Unknown")
                : null,
            CreatedAt = o.CreatedAt,
            CompletedAt = o.CompletedAt,
            CancelledAt = o.CancelledAt,
            ReturnedAt = lastReturn?.ReturnedAt,
            ReturnedByUsername = lastReturn?.ReturnedByUsername,
            ReturnReason = lastReturn?.Reason,
            RefundedAmount = returns.Count > 0 ? returns.Sum(r => r.RefundAmount) : null,
            ReturnedValue = returns.Sum(r => r.GoodsValue),
            Returns = returns
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] OrderStatus? status,
        [FromQuery] int? customerId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var query = _db.Orders.Include(o => o.Customer).AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(o =>
                o.OrderNumber.ToLower().Contains(term) ||
                (o.Customer != null && o.Customer.Name.ToLower().Contains(term)) ||
                (o.Customer != null && o.Customer.Phone.Contains(term)));
        }

        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (customerId.HasValue) query = query.Where(o => o.CustomerId == customerId.Value);
        if (fromDate.HasValue) query = query.Where(o => o.OrderDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(o => o.OrderDate <= toDate.Value);

        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase) || sortBy is null;

        query = sortBy?.ToLower() switch
        {
            "totalamount" => descending ? query.OrderByDescending(o => o.TotalAmount) : query.OrderBy(o => o.TotalAmount),
            "orderdate" => descending ? query.OrderByDescending(o => o.OrderDate) : query.OrderBy(o => o.OrderDate),
            _ => query.OrderByDescending(o => o.CreatedAt),
        };

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var orders = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var userIds = orders.Select(o => o.CreatedByUserId).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName }).ToListAsync();
        var userLookup = users.ToDictionary(u => u.Id, u => u.UserName ?? "Unknown");

        var items = orders.Select(o => new OrderListItemResponse
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            CustomerName = o.Customer?.Name ?? "Walk-in Customer",
            OrderDate = o.OrderDate,
            Status = o.Status,
            TotalAmount = o.TotalAmount,
            CreatedByUsername = userLookup.GetValueOrDefault(o.CreatedByUserId, "Unknown")
        }).ToList();

        return Ok(new PagedResult<OrderListItemResponse>
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
        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null) return NotFound(new { message = "Order not found." });

        return Ok(await ToResponseAsync(order));
    }

    private async Task<(bool Ok, IActionResult? Error, List<OrderItem> Items, decimal Subtotal)> BuildItemsAsync(
        List<OrderItemRequest> requestItems)
    {
        var productIds = requestItems.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();

        if (products.Count != productIds.Count)
        {
            return (false, BadRequest(new { message = "One or more products do not exist." }), [], 0);
        }

        var inactive = products.FirstOrDefault(p => !p.IsActive);
        if (inactive is not null)
        {
            return (false, BadRequest(new { message = $"Product '{inactive.Name}' is not active." }), [], 0);
        }

        decimal subtotal = 0;
        var items = new List<OrderItem>();

        foreach (var reqItem in requestItems)
        {
            var product = products.First(p => p.Id == reqItem.ProductId);
            var baseTotal = reqItem.Quantity * product.SalePrice;
            var lineTotal = Math.Max(0, baseTotal - reqItem.DiscountAmount);
            subtotal += lineTotal;

            items.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = reqItem.Quantity,
                UnitPrice = product.SalePrice, // server-side snapshot, never trust client price
                DiscountAmount = reqItem.DiscountAmount,
                LineTotal = lineTotal
            });
        }

        return (true, null, items, subtotal);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Cashier")]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (request.CustomerId.HasValue)
        {
            var customerExists = await _db.Customers.AnyAsync(c => c.Id == request.CustomerId.Value);
            if (!customerExists) return BadRequest(new { message = "Customer does not exist." });
        }

        var (ok, error, items, subtotal) = await BuildItemsAsync(request.Items);
        if (!ok) return error!;

        if (request.DiscountAmount > subtotal)
        {
            return BadRequest(new { message = "Discount cannot exceed subtotal." });
        }

        var orderNumber = await OrderNumberGenerator.GenerateAsync(_db);

        var order = new Order
        {
            OrderNumber = orderNumber,
            CustomerId = request.CustomerId,
            Status = OrderStatus.Draft,
            OrderDate = DateTime.UtcNow,
            Notes = request.Notes,
            Subtotal = subtotal,
            DiscountAmount = request.DiscountAmount,
            TotalAmount = Math.Max(0, subtotal - request.DiscountAmount),
            CreatedByUserId = CurrentUserId!,
            CreatedAt = DateTime.UtcNow,
            Items = items
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, "Order.Create", "Order", order.Id.ToString(),
            $"Order {order.OrderNumber} created (Draft), total {order.TotalAmount}.");

        var created = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstAsync(o => o.Id == order.Id);

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, await ToResponseAsync(created));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager,Cashier")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateOrderRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound(new { message = "Order not found." });

        if (order.Status != OrderStatus.Draft)
        {
            return Conflict(new { message = "Only draft orders can be edited." });
        }

        if (request.CustomerId.HasValue)
        {
            var customerExists = await _db.Customers.AnyAsync(c => c.Id == request.CustomerId.Value);
            if (!customerExists) return BadRequest(new { message = "Customer does not exist." });
        }

        var (ok, error, items, subtotal) = await BuildItemsAsync(request.Items);
        if (!ok) return error!;

        if (request.DiscountAmount > subtotal)
        {
            return BadRequest(new { message = "Discount cannot exceed subtotal." });
        }

        _db.OrderItems.RemoveRange(order.Items);

        order.CustomerId = request.CustomerId;
        order.Notes = request.Notes;
        order.Subtotal = subtotal;
        order.DiscountAmount = request.DiscountAmount;
        order.TotalAmount = Math.Max(0, subtotal - request.DiscountAmount);
        order.Items = items;

        await _db.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, "Order.Update", "Order", order.Id.ToString(),
            $"Order {order.OrderNumber} updated.");

        var updated = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstAsync(o => o.Id == order.Id);

        return Ok(await ToResponseAsync(updated));
    }

    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = "Admin,Manager,Cashier")]
    public async Task<IActionResult> Complete(int id, [FromBody] CompleteOrderRequest? request = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var ip = request?.InitialPayment;
        var initialPayment = ip is null
            ? null
            : new InitialPaymentInput(ip.Amount, ip.Method, ip.ReferenceNumber, ip.Notes);

        try
        {
            var order = await _orderService.CompleteAsync(id, CurrentUserId!, initialPayment);

            await _auditService.LogAsync(CurrentUserId, "Order.Complete", "Order", order.Id.ToString(),
                $"Order {order.OrderNumber} completed. Total: {order.TotalAmount}, Items: {order.Items.Count}.");

            if (initialPayment is not null)
            {
                await _auditService.LogAsync(CurrentUserId, "Payment.Create", "Order", order.Id.ToString(),
                    $"Initial payment of {initialPayment.Amount} ({initialPayment.Method}) recorded atomically with completion of {order.OrderNumber}.");
            }

            var completed = await _db.Orders
                .Include(o => o.Customer)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .AsNoTracking()
                .FirstAsync(o => o.Id == id);

            return Ok(await ToResponseAsync(completed));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Order not found." });
        }
        catch (OrderAlreadyCompletedException)
        {
            return Conflict(new { message = "Order has already been completed or is not a draft." });
        }
        catch (InsufficientStockForSaleException ex)
        {
            return Conflict(new
            {
                message = "Insufficient stock for one or more products.",
                code = "INSUFFICIENT_STOCK",
                shortages = ex.Shortages.Select(s => new
                {
                    productId = s.ProductId,
                    productName = s.ProductName,
                    available = s.Available,
                    requested = s.Requested
                })
            });
        }
        catch (WalkInCreditNotAllowedException ex)
        {
            return BadRequest(new { message = ex.Message, code = "WALKIN_FULL_PAYMENT_REQUIRED" });
        }
        catch (PaymentOverpaymentException ex)
        {
            return Conflict(new
            {
                message = "Payment exceeds the order total.",
                code = "PAYMENT_EXCEEDS_TOTAL",
                outstanding = ex.Outstanding,
                requested = ex.Requested
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = "Admin,Manager,Cashier")]
    public async Task<IActionResult> Cancel(int id)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound(new { message = "Order not found." });

        if (order.Status == OrderStatus.Completed)
        {
            return Conflict(new
            {
                message = "Completed sale cannot be cancelled directly. Use a return/reversal workflow."
            });
        }

        try
        {
            await _orderService.CancelDraftAsync(id);
        }
        catch (OrderNotDraftException)
        {
            return Conflict(new { message = "Only draft orders can be cancelled." });
        }

        await _auditService.LogAsync(CurrentUserId, "Order.Cancel", "Order", order.Id.ToString(),
            $"Order {order.OrderNumber} cancelled.");

        return NoContent();
    }

    /// <summary>
    /// Return goods of a sold order, whole or in part (Admin/Manager via Sales.Return). Each call is one immutable
    /// return operation dated now; only sellable units go back to stock; the refund is recorded as money paid back.
    /// </summary>
    [HttpPost("{id:int}/return")]
    [Authorize(Policy = Permissions.SalesReturn)]
    public async Task<IActionResult> Return(int id, [FromBody] ReturnOrderRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var input = new ReturnInput(
                request.Reason,
                request.Items?.Select(i => new ReturnLineInput(i.OrderItemId, i.Quantity, i.DamagedQuantity)).ToList(),
                request.RefundMethod,
                request.RefundAmount);

            var result = await _orderService.ReturnAsync(id, CurrentUserId!, input);
            var ret = result.Return;

            await _auditService.LogAsync(CurrentUserId, "Order.Return", "Order", id.ToString(),
                $"Return {ret.ReturnNumber} on order {result.Order.OrderNumber} ({(result.Order.Status == OrderStatus.Returned ? "order fully returned" : "partial")}). " +
                $"Lines: {ret.Items.Count}, goods value: {ret.GoodsValue}, restocked: {ret.Items.Sum(i => i.RestockedQuantity)}, written off: {ret.Items.Sum(i => i.WrittenOffQuantity)}, " +
                $"refunded: {ret.RefundAmount}{(ret.RefundMethod is null ? "" : $" ({ret.RefundMethod})")}. Reason: {request.Reason.Trim()}",
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

            var returned = await _db.Orders
                .Include(o => o.Customer)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .AsNoTracking()
                .FirstAsync(o => o.Id == id);

            return Ok(await ToResponseAsync(returned));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Order not found." });
        }
        catch (OrderNotReturnableException ex)
        {
            return Conflict(new
            {
                message = "Only completed or partially returned orders can be returned.",
                code = "ORDER_NOT_RETURNABLE",
                status = ex.Status.ToString()
            });
        }
        catch (InvalidReturnException ex)
        {
            return BadRequest(new { message = ex.Message, code = ex.Code });
        }
    }

    [HttpGet("{id:int}/payments")]
    public async Task<IActionResult> GetPayments(int id)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound(new { message = "Order not found." });

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.OrderId == id)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        // Money position after returns (derived, never stored): see OrderBalance.
        var balance = await OrderBalance.LoadAsync(_db, order.Id, order.TotalAmount);
        var totalPaid = balance.Paid;
        var outstanding = balance.Outstanding;

        var userIds = payments.Select(p => p.ReceivedByUserId).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName }).ToListAsync();
        var userLookup = users.ToDictionary(u => u.Id, u => u.UserName ?? "Unknown");

        return Ok(new
        {
            orderTotal = order.TotalAmount,
            totalPaid,
            outstandingBalance = outstanding,
            refundedTotal = balance.Refunded,
            returnedValue = balance.ReturnedValue,
            paymentState = order.Status == OrderStatus.Returned ? "Refunded"
                : balance.NetPaid <= 0 ? "Unpaid" : balance.NetPaid < balance.NetTotal ? "PartiallyPaid" : "Paid",
            payments = payments.Select(p => new
            {
                p.Id,
                p.PaymentNumber,
                p.CreatedAt,
                p.Amount,
                p.Method,
                p.Status,
                p.ReferenceNumber,
                receivedByUsername = userLookup.GetValueOrDefault(p.ReceivedByUserId, "Unknown")
            })
        });
    }


}