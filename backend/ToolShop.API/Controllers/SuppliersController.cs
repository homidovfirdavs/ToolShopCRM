using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Catalog;
using ToolShop.API.DTOs.Purchases;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Infrastructure.Audit;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _auditService;
    private const int MaxPageSize = 100;

    public SuppliersController(AppDbContext db, AuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    private static SupplierResponse ToResponse(Supplier s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        ContactPerson = s.ContactPerson,
        Phone = s.Phone,
        Email = s.Email,
        Address = s.Address,
        Notes = s.Notes,
        IsActive = s.IsActive,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt
    };

    private static SupplierListItemResponse ToListItem(Supplier s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        ContactPerson = s.ContactPerson,
        Phone = s.Phone,
        Email = s.Email,
        IsActive = s.IsActive
    };

    private string? CurrentUserId =>
        User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var query = _db.Suppliers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s =>
                s.Name.ToLower().Contains(term) ||
                (s.Phone != null && s.Phone.Contains(term)) ||
                (s.ContactPerson != null && s.ContactPerson.ToLower().Contains(term)));
        }

        if (isActive.HasValue)
        {
            query = query.Where(s => s.IsActive == isActive.Value);
        }

        query = query.OrderBy(s => s.Name);

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => ToListItem(s))
            .ToListAsync();

        return Ok(new PagedResult<SupplierListItemResponse>
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
        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound(new { message = "Supplier not found." });

        return Ok(ToResponse(supplier));
    }

    [HttpGet("{id:int}/purchases")]
    public async Task<IActionResult> GetPurchases(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var supplierExists = await _db.Suppliers.AnyAsync(s => s.Id == id);
        if (!supplierExists) return NotFound(new { message = "Supplier not found." });

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var query = _db.Purchases.AsNoTracking().Where(p => p.SupplierId == id)
            .OrderByDescending(p => p.PurchaseDate);

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.PurchaseNumber,
                p.PurchaseDate,
                p.Status,
                p.TotalAmount
            })
            .ToListAsync();

        return Ok(new { items, page, pageSize, totalItems, totalPages });
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Warehouse")]
    public async Task<IActionResult> Create([FromBody] CreateSupplierRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var supplier = new Supplier
        {
            Name = request.Name.Trim(),
            ContactPerson = request.ContactPerson,
            Phone = request.Phone,
            Email = request.Email,
            Address = request.Address,
            Notes = request.Notes,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, "Supplier.Create", "Supplier", supplier.Id.ToString(),
            $"Supplier '{supplier.Name}' created.");

        return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, ToResponse(supplier));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager,Warehouse")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSupplierRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound(new { message = "Supplier not found." });

        supplier.Name = request.Name.Trim();
        supplier.ContactPerson = request.ContactPerson;
        supplier.Phone = request.Phone;
        supplier.Email = request.Email;
        supplier.Address = request.Address;
        supplier.Notes = request.Notes;
        supplier.IsActive = request.IsActive;
        supplier.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, "Supplier.Update", "Supplier", supplier.Id.ToString(),
            $"Supplier '{supplier.Name}' updated.");

        return Ok(ToResponse(supplier));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound(new { message = "Supplier not found." });

        supplier.IsActive = false;
        supplier.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, "Supplier.Deactivate", "Supplier", supplier.Id.ToString(),
            $"Supplier '{supplier.Name}' deactivated.");

        return NoContent();
    }
}