using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Catalog;
using ToolShop.API.DTOs.Customers;
using ToolShop.Domain.Entities.Customers;
using ToolShop.Infrastructure.Audit;
using ToolShop.Infrastructure.Helpers;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Roles = "Admin,Manager,Cashier")]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _auditService;
    private const int MaxPageSize = 100;

    public CustomersController(AppDbContext db, AuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    private static CustomerResponse ToResponse(Customer c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Phone = c.Phone,
        Address = c.Address,
        Notes = c.Notes,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };

    private static CustomerListItemResponse ToListItem(Customer c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Phone = c.Phone,
        Address = c.Address,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt
    };

    private string? CurrentUserId =>
        User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var query = _db.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var normalizedTerm = PhoneNormalizer.Normalize(term);
            var lowerTerm = term.ToLower();

            query = query.Where(c =>
                c.Name.ToLower().Contains(lowerTerm) ||
                (normalizedTerm != "" && c.NormalizedPhone.Contains(normalizedTerm)));
        }

        if (isActive.HasValue)
        {
            query = query.Where(c => c.IsActive == isActive.Value);
        }

        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortBy?.ToLower() switch
        {
            "createdat" => descending ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            _ => descending ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
        };

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => ToListItem(c))
            .ToListAsync();

        return Ok(new PagedResult<CustomerListItemResponse>
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
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);

        if (customer is null)
        {
            return NotFound(new { message = "Customer not found." });
        }

        return Ok(ToResponse(customer));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Cashier")]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normalizedPhone = PhoneNormalizer.Normalize(request.Phone);

        if (string.IsNullOrEmpty(normalizedPhone))
        {
            return BadRequest(new { message = "A valid phone number is required." });
        }

        var exists = await _db.Customers.AnyAsync(c => c.NormalizedPhone == normalizedPhone);
        if (exists)
        {
            return Conflict(new { message = "Customer with this phone number already exists." });
        }

        var customer = new Customer
        {
            Name = request.Name.Trim(),
            Phone = request.Phone.Trim(),
            NormalizedPhone = normalizedPhone,
            Address = request.Address,
            Notes = request.Notes,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, "Customer.Create", "Customer", customer.Id.ToString(),
            $"Customer '{customer.Name}' created.");

        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, ToResponse(customer));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager,Cashier")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null)
        {
            return NotFound(new { message = "Customer not found." });
        }

        var normalizedPhone = PhoneNormalizer.Normalize(request.Phone);

        if (string.IsNullOrEmpty(normalizedPhone))
        {
            return BadRequest(new { message = "A valid phone number is required." });
        }

        var duplicate = await _db.Customers.AnyAsync(c => c.NormalizedPhone == normalizedPhone && c.Id != id);
        if (duplicate)
        {
            return Conflict(new { message = "Customer with this phone number already exists." });
        }

        customer.Name = request.Name.Trim();
        customer.Phone = request.Phone.Trim();
        customer.NormalizedPhone = normalizedPhone;
        customer.Address = request.Address;
        customer.Notes = request.Notes;
        customer.IsActive = request.IsActive;
        customer.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, "Customer.Update", "Customer", customer.Id.ToString(),
            $"Customer '{customer.Name}' updated.");

        return Ok(ToResponse(customer));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null)
        {
            return NotFound(new { message = "Customer not found." });
        }

        customer.IsActive = false;
        customer.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, "Customer.Deactivate", "Customer", customer.Id.ToString(),
            $"Customer '{customer.Name}' deactivated.");

        return NoContent();
    }
    [HttpGet("{id:int}/orders")]
    public async Task<IActionResult> GetOrders(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var customerExists = await _db.Customers.AnyAsync(c => c.Id == id);
        if (!customerExists) return NotFound(new { message = "Customer not found." });

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _db.Orders.AsNoTracking().Where(o => o.CustomerId == id)
            .OrderByDescending(o => o.OrderDate);

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.OrderDate,
                o.Status,
                o.TotalAmount
            })
            .ToListAsync();

        return Ok(new { items, page, pageSize, totalItems, totalPages });
    }

    [HttpGet("{id:int}/payments")]
    public async Task<IActionResult> GetPayments(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var customerExists = await _db.Customers.AnyAsync(c => c.Id == id);
        if (!customerExists) return NotFound(new { message = "Customer not found." });

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _db.Payments.Include(p => p.Order).AsNoTracking()
            .Where(p => p.CustomerId == id)
            .OrderByDescending(p => p.CreatedAt);

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var payments = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var userIds = payments.Select(p => p.ReceivedByUserId).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName }).ToListAsync();
        var userLookup = users.ToDictionary(u => u.Id, u => u.UserName ?? "Unknown");

        var items = payments.Select(p => new
        {
            p.Id,
            p.PaymentNumber,
            orderNumber = p.Order.OrderNumber,
            p.CreatedAt,
            p.Amount,
            p.Method,
            p.Status,
            receivedByUsername = userLookup.GetValueOrDefault(p.ReceivedByUserId, "Unknown")
        });

        return Ok(new { items, page, pageSize, totalItems, totalPages });
    }

    [HttpGet("{id:int}/outstanding-orders")]
    public async Task<IActionResult> GetOutstandingOrders(int id, [FromServices] Infrastructure.Payments.PaymentService paymentService)
    {
        var customerExists = await _db.Customers.AnyAsync(c => c.Id == id);
        if (!customerExists) return NotFound(new { message = "Customer not found." });

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == id && Infrastructure.Sales.SalesFilters.Receivable.Contains(o.Status))
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        var result = new List<object>();
        foreach (var order in orders)
        {
            var balance = await Infrastructure.Sales.OrderBalance.LoadAsync(_db, order.Id, order.TotalAmount);
            var outstanding = balance.Outstanding;
            if (outstanding > 0)
            {
                var paid = balance.NetPaid;
                result.Add(new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    orderDate = order.OrderDate,
                    total = order.TotalAmount,
                    paid,
                    outstanding,
                    paymentState = paid <= 0 ? "Unpaid" : "PartiallyPaid"
                });
            }
        }

        return Ok(result);
    }
}