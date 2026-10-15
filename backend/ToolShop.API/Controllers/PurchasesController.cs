using Microsoft.AspNetCore.Authorization;
using ToolShop.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Catalog;
using ToolShop.API.DTOs.Purchases;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Infrastructure.Audit;
using ToolShop.Infrastructure.Persistence;
using ToolShop.Infrastructure.Purchases;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/purchases")]
[Authorize]
public class PurchasesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PurchaseService _purchaseService;
    private readonly AuditService _auditService;
    private const int MaxPageSize = 100;

    public PurchasesController(AppDbContext db, PurchaseService purchaseService, AuditService auditService)
    {
        _db = db;
        _purchaseService = purchaseService;
        _auditService = auditService;
    }

    private string? CurrentUserId =>
        User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

    private async Task<PurchaseResponse> ToResponseAsync(Purchase p)
    {
        var userIds = new List<string> { p.CreatedByUserId };
        if (p.ConfirmedByUserId is not null) userIds.Add(p.ConfirmedByUserId);

        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToListAsync();
        var userLookup = users.ToDictionary(u => u.Id, u => u.UserName ?? "Unknown");

        return new PurchaseResponse
        {
            Id = p.Id,
            PurchaseNumber = p.PurchaseNumber,
            SupplierId = p.SupplierId,
            SupplierName = p.Supplier.Name,
            Status = p.Status,
            PurchaseDate = p.PurchaseDate,
            Notes = p.Notes,
            Subtotal = p.Subtotal,
            DiscountAmount = p.DiscountAmount,
            TotalAmount = p.TotalAmount,
            Items = p.Items.Select(i => new PurchaseItemResponse
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.Product.Name,
                SKU = i.Product.SKU,
                Quantity = i.Quantity,
                UnitCost = i.UnitCost,
                LineTotal = i.LineTotal
            }).ToList(),
            CreatedByUserId = p.CreatedByUserId,
            CreatedByUsername = userLookup.GetValueOrDefault(p.CreatedByUserId, "Unknown"),
            ConfirmedByUserId = p.ConfirmedByUserId,
            ConfirmedByUsername = p.ConfirmedByUserId is not null
                ? userLookup.GetValueOrDefault(p.ConfirmedByUserId, "Unknown")
                : null,
            CreatedAt = p.CreatedAt,
            ConfirmedAt = p.ConfirmedAt,
            CancelledAt = p.CancelledAt
        };
    }

    [HttpGet]
    [Authorize(Policy = Permissions.InventoryView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? supplierId,
        [FromQuery] PurchaseStatus? status,
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

        var query = _db.Purchases.Include(p => p.Supplier).AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p =>
                p.PurchaseNumber.ToLower().Contains(term) ||
                p.Supplier.Name.ToLower().Contains(term));
        }

        if (supplierId.HasValue) query = query.Where(p => p.SupplierId == supplierId.Value);
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        if (fromDate.HasValue) query = query.Where(p => p.PurchaseDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(p => p.PurchaseDate <= toDate.Value);

        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase) || sortBy is null;

        query = sortBy?.ToLower() switch
        {
            "totalamount" => descending ? query.OrderByDescending(p => p.TotalAmount) : query.OrderBy(p => p.TotalAmount),
            "purchasedate" => descending ? query.OrderByDescending(p => p.PurchaseDate) : query.OrderBy(p => p.PurchaseDate),
            _ => query.OrderByDescending(p => p.CreatedAt),
        };

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var purchases = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var userIds = purchases.Select(p => p.CreatedByUserId).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName }).ToListAsync();
        var userLookup = users.ToDictionary(u => u.Id, u => u.UserName ?? "Unknown");

        var items = purchases.Select(p => new PurchaseListItemResponse
        {
            Id = p.Id,
            PurchaseNumber = p.PurchaseNumber,
            SupplierName = p.Supplier.Name,
            PurchaseDate = p.PurchaseDate,
            Status = p.Status,
            TotalAmount = p.TotalAmount,
            CreatedByUsername = userLookup.GetValueOrDefault(p.CreatedByUserId, "Unknown"),
            CreatedAt = p.CreatedAt
        }).ToList();

        return Ok(new PagedResult<PurchaseListItemResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        });
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.InventoryView)]
    public async Task<IActionResult> GetById(int id)
    {
        var purchase = await _db.Purchases
            .Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (purchase is null) return NotFound(new { message = "Purchase not found." });

        return Ok(await ToResponseAsync(purchase));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Warehouse")]
    public async Task<IActionResult> Create([FromBody] CreatePurchaseRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId);
        if (supplier is null) return BadRequest(new { message = "Supplier does not exist." });

        var productIds = request.Items.Select(i => i.ProductId).ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
        if (products.Count != productIds.Distinct().Count())
        {
            return BadRequest(new { message = "One or more products do not exist." });
        }

        decimal subtotal = 0;
        var items = new List<PurchaseItem>();
        foreach (var itemRequest in request.Items)
        {
            var lineTotal = itemRequest.Quantity * itemRequest.UnitCost;
            subtotal += lineTotal;
            items.Add(new PurchaseItem
            {
                ProductId = itemRequest.ProductId,
                Quantity = itemRequest.Quantity,
                UnitCost = itemRequest.UnitCost,
                LineTotal = lineTotal
            });
        }

        if (request.DiscountAmount > subtotal)
        {
            return BadRequest(new { message = "Discount cannot exceed subtotal." });
        }

        var purchaseNumber = await PurchaseNumberGenerator.GenerateAsync(_db);

        var purchase = new Purchase
        {
            PurchaseNumber = purchaseNumber,
            SupplierId = request.SupplierId,
            Status = PurchaseStatus.Draft,
                        PurchaseDate = DateTime.SpecifyKind(request.PurchaseDate, DateTimeKind.Utc),
            Notes = request.Notes,
            Subtotal = subtotal,
            DiscountAmount = request.DiscountAmount,
            TotalAmount = subtotal - request.DiscountAmount,
            CreatedByUserId = CurrentUserId!,
            CreatedAt = DateTime.UtcNow,
            Items = items
        };

        _db.Purchases.Add(purchase);
        await _db.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, "Purchase.Create", "Purchase", purchase.Id.ToString(),
            $"Purchase {purchase.PurchaseNumber} created (Draft), supplier '{supplier.Name}', total {purchase.TotalAmount}.");

        var created = await _db.Purchases
            .Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .FirstAsync(p => p.Id == purchase.Id);

        return CreatedAtAction(nameof(GetById), new { id = purchase.Id }, await ToResponseAsync(created));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager,Warehouse")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePurchaseRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var purchase = await _db.Purchases.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
        if (purchase is null) return NotFound(new { message = "Purchase not found." });

        if (purchase.Status != PurchaseStatus.Draft)
        {
            return Conflict(new { message = "Only draft purchases can be edited." });
        }

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId);
        if (supplier is null) return BadRequest(new { message = "Supplier does not exist." });

        var productIds = request.Items.Select(i => i.ProductId).ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
        if (products.Count != productIds.Distinct().Count())
        {
            return BadRequest(new { message = "One or more products do not exist." });
        }

        _db.PurchaseItems.RemoveRange(purchase.Items);

        decimal subtotal = 0;
        var newItems = new List<PurchaseItem>();
        foreach (var itemRequest in request.Items)
        {
            var lineTotal = itemRequest.Quantity * itemRequest.UnitCost;
            subtotal += lineTotal;
            newItems.Add(new PurchaseItem
            {
                PurchaseId = purchase.Id,
                ProductId = itemRequest.ProductId,
                Quantity = itemRequest.Quantity,
                UnitCost = itemRequest.UnitCost,
                LineTotal = lineTotal
            });
        }

        if (request.DiscountAmount > subtotal)
        {
            return BadRequest(new { message = "Discount cannot exceed subtotal." });
        }

        purchase.SupplierId = request.SupplierId;
                purchase.PurchaseDate = DateTime.SpecifyKind(request.PurchaseDate, DateTimeKind.Utc);
        purchase.Notes = request.Notes;
        purchase.Subtotal = subtotal;
        purchase.DiscountAmount = request.DiscountAmount;
        purchase.TotalAmount = subtotal - request.DiscountAmount;
        purchase.Items = newItems;

        await _db.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, "Purchase.Update", "Purchase", purchase.Id.ToString(),
            $"Purchase {purchase.PurchaseNumber} updated.");

        var updated = await _db.Purchases
            .Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .FirstAsync(p => p.Id == purchase.Id);

        return Ok(await ToResponseAsync(updated));
    }

    [HttpPost("{id:int}/confirm")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Confirm(int id)
    {
        try
        {
            var purchase = await _purchaseService.ConfirmAsync(id, CurrentUserId!);

            await _auditService.LogAsync(CurrentUserId, "Purchase.Confirm", "Purchase", purchase.Id.ToString(),
                $"Purchase {purchase.PurchaseNumber} confirmed. Supplier: {purchase.SupplierId}, Total: {purchase.TotalAmount}.");

            var confirmed = await _db.Purchases
                .Include(p => p.Supplier)
                .Include(p => p.Items).ThenInclude(i => i.Product)
                .AsNoTracking()
                .FirstAsync(p => p.Id == id);

            return Ok(await ToResponseAsync(confirmed));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Purchase not found." });
        }
        catch (PurchaseAlreadyConfirmedException)
        {
            return Conflict(new { message = "Purchase has already been confirmed or is not a draft." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = "Admin,Manager,Warehouse")]
    public async Task<IActionResult> Cancel(int id)
    {
        var purchase = await _db.Purchases.FirstOrDefaultAsync(p => p.Id == id);
        if (purchase is null) return NotFound(new { message = "Purchase not found." });

        if (purchase.Status == PurchaseStatus.Confirmed)
        {
            return Conflict(new
            {
                message = "Confirmed purchase cannot be cancelled directly. Use a purchase return/reversal workflow."
            });
        }

        try
        {
            await _purchaseService.CancelDraftAsync(id);
        }
        catch (PurchaseNotDraftException)
        {
            return Conflict(new { message = "Only draft purchases can be cancelled." });
        }

        await _auditService.LogAsync(CurrentUserId, "Purchase.Cancel", "Purchase", purchase.Id.ToString(),
            $"Purchase {purchase.PurchaseNumber} cancelled.");

        return NoContent();
    }
}