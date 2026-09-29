using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Catalog;
using ToolShop.API.DTOs.Inventory;
using ToolShop.Domain.Entities;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Infrastructure.Audit;
using ToolShop.Infrastructure.Inventory;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly InventoryService _inventoryService;
    private readonly AuditService _auditService;
    private readonly UserManager<ApplicationUser> _userManager;
    private const int MaxPageSize = 100;

    public InventoryController(
        AppDbContext db,
        InventoryService inventoryService,
        AuditService auditService,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _inventoryService = inventoryService;
        _auditService = auditService;
        _userManager = userManager;
    }

    private string? CurrentUserId =>
        User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

    private static StockStatus ResolveStatus(int currentStock, int minimumStockLevel)
    {
        if (currentStock <= 0) return StockStatus.OutOfStock;
        if (currentStock <= minimumStockLevel) return StockStatus.LowStock;
        return StockStatus.InStock;
    }

    // ---------- Stock mutations ----------

    [HttpPost("stock-in")]
    [Authorize(Roles = "Admin,Manager,Warehouse")]
    public async Task<IActionResult> StockIn([FromBody] StockMutationRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId);
        if (product is null) return NotFound(new { message = "Product not found." });
        if (!product.IsActive) return BadRequest(new { message = "Product is not active." });

        var movement = await _inventoryService.CreateStockInAsync(
            request.ProductId, request.Quantity, request.Reason, request.Notes, CurrentUserId!);

        await _auditService.LogAsync(CurrentUserId, "Inventory.StockIn", "Product", product.Id.ToString(),
            $"Stock in: +{request.Quantity} for '{product.Name}'. Reason: {request.Reason}");

        return Ok(new { movement.Id, movement.CreatedAt });
    }

    [HttpPost("stock-out")]
    [Authorize(Roles = "Admin,Manager,Warehouse")]
    public async Task<IActionResult> StockOut([FromBody] StockMutationRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId);
        if (product is null) return NotFound(new { message = "Product not found." });

        try
        {
            var movement = await _inventoryService.CreateStockOutAsync(
                request.ProductId, request.Quantity, request.Reason, request.Notes, CurrentUserId!);

            await _auditService.LogAsync(CurrentUserId, "Inventory.StockOut", "Product", product.Id.ToString(),
                $"Stock out: -{request.Quantity} for '{product.Name}'. Reason: {request.Reason}");

            return Ok(new { movement.Id, movement.CreatedAt });
        }
        catch (InsufficientStockException ex)
        {
            return Conflict(new { message = "Insufficient stock.", available = ex.Available, requested = ex.Requested });
        }
    }

    [HttpPost("adjustment")]
    [Authorize(Roles = "Admin,Manager,Warehouse")]
    public async Task<IActionResult> Adjustment([FromBody] AdjustmentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (request.Type != InventoryMovementType.AdjustmentIncrease &&
            request.Type != InventoryMovementType.AdjustmentDecrease)
        {
            return BadRequest(new { message = "Type must be AdjustmentIncrease or AdjustmentDecrease." });
        }

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId);
        if (product is null) return NotFound(new { message = "Product not found." });

        var previousStock = await _inventoryService.GetCurrentStockAsync(request.ProductId);

        try
        {
            var movement = await _inventoryService.CreateAdjustmentAsync(
                request.ProductId, request.Type, request.Quantity, request.Reason, request.Notes, CurrentUserId!);

            var newStock = await _inventoryService.GetCurrentStockAsync(request.ProductId);

            await _auditService.LogAsync(CurrentUserId, "Inventory.Adjustment", "Product", product.Id.ToString(),
                $"Adjustment ({request.Type}) for '{product.Name}': {previousStock} -> {newStock}. Reason: {request.Reason}");

            return Ok(new { movement.Id, movement.CreatedAt, previousStock, newStock });
        }
        catch (InsufficientStockException ex)
        {
            return Conflict(new { message = "Insufficient stock.", available = ex.Available, requested = ex.Requested });
        }
    }

    // ---------- Stock views ----------

    [HttpGet("stock")]
    public async Task<IActionResult> GetStock(
        [FromQuery] string? search,
        [FromQuery] int? categoryId,
        [FromQuery] int? brandId,
        [FromQuery] StockStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var productQuery = _db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .AsNoTracking()
            .Where(p => p.IsActive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            productQuery = productQuery.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.SKU.ToLower().Contains(term) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(term)));
        }

        if (categoryId.HasValue) productQuery = productQuery.Where(p => p.CategoryId == categoryId.Value);
        if (brandId.HasValue) productQuery = productQuery.Where(p => p.BrandId == brandId.Value);

        var products = await productQuery.OrderBy(p => p.Name).ToListAsync();
        var productIds = products.Select(p => p.Id).ToList();

        var stockByProduct = await _inventoryService.GetCurrentStockBulkAsync(productIds);

        var allItems = products.Select(p => new StockItemResponse
        {
            ProductId = p.Id,
            ProductName = p.Name,
            SKU = p.SKU,
            Barcode = p.Barcode,
            CategoryName = p.Category.Name,
            BrandName = p.Brand.Name,
            CurrentStock = stockByProduct.GetValueOrDefault(p.Id, 0),
            MinimumStockLevel = p.MinimumStockLevel,
            Status = ResolveStatus(stockByProduct.GetValueOrDefault(p.Id, 0), p.MinimumStockLevel)
        }).ToList();

        if (status.HasValue)
        {
            allItems = allItems.Where(i => i.Status == status.Value).ToList();
        }

        var totalItems = allItems.Count;
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var pageItems = allItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new PagedResult<StockItemResponse>
        {
            Items = pageItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        });
    }

    [HttpGet("stock/{productId:int}")]
    public async Task<IActionResult> GetSingleStock(int productId)
    {
        var product = await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product is null) return NotFound(new { message = "Product not found." });

        var currentStock = await _inventoryService.GetCurrentStockAsync(productId);

        return Ok(new StockItemResponse
        {
            ProductId = product.Id,
            ProductName = product.Name,
            SKU = product.SKU,
            Barcode = product.Barcode,
            CategoryName = product.Category.Name,
            BrandName = product.Brand.Name,
            CurrentStock = currentStock,
            MinimumStockLevel = product.MinimumStockLevel,
            Status = ResolveStatus(currentStock, product.MinimumStockLevel)
        });
    }

    [HttpGet("low-stock")]
    public async Task<IActionResult> GetLowStock()
    {
        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync();

        var stockByProduct = await _inventoryService.GetCurrentStockBulkAsync(products.Select(p => p.Id));

        var items = products
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.SKU,
                CurrentStock = stockByProduct.GetValueOrDefault(p.Id, 0),
                p.MinimumStockLevel
            })
            .Where(x => x.CurrentStock <= x.MinimumStockLevel)
            .OrderBy(x => x.CurrentStock)
            .ToList();

        return Ok(items);
    }

    [HttpGet("out-of-stock")]
    public async Task<IActionResult> GetOutOfStock()
    {
        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync();

        var stockByProduct = await _inventoryService.GetCurrentStockBulkAsync(products.Select(p => p.Id));

        var items = products
            .Select(p => new { p.Id, p.Name, p.SKU, CurrentStock = stockByProduct.GetValueOrDefault(p.Id, 0) })
            .Where(x => x.CurrentStock <= 0)
            .OrderBy(x => x.Name)
            .ToList();

        return Ok(items);
    }

    // ---------- Movement history ----------

    [HttpGet("movements")]
    public async Task<IActionResult> GetMovements(
        [FromQuery] int? productId,
        [FromQuery] InventoryMovementType? movementType,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var query = _db.InventoryMovements
            .Include(m => m.Product)
            .AsNoTracking()
            .AsQueryable();

        if (productId.HasValue) query = query.Where(m => m.ProductId == productId.Value);
        if (movementType.HasValue) query = query.Where(m => m.MovementType == movementType.Value);
        if (fromDate.HasValue) query = query.Where(m => m.CreatedAt >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(m => m.CreatedAt <= toDate.Value);

        query = query.OrderByDescending(m => m.CreatedAt);

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var movements = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MovementResponse
            {
                Id = m.Id,
                CreatedAt = m.CreatedAt,
                ProductId = m.ProductId,
                ProductName = m.Product.Name,
                SKU = m.Product.SKU,
                MovementType = m.MovementType,
                Quantity = m.Quantity,
                Reason = m.Reason,
                Notes = m.Notes,
                ReferenceType = m.ReferenceType,
                ReferenceId = m.ReferenceId,
                CreatedByUserId = m.CreatedByUserId
            })
            .ToListAsync();

        var userIds = movements.Select(m => m.CreatedByUserId).Distinct().ToList();
        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToListAsync();
        var userLookup = users.ToDictionary(u => u.Id, u => u.UserName ?? "Unknown");

        foreach (var m in movements)
        {
            m.CreatedByUsername = userLookup.GetValueOrDefault(m.CreatedByUserId, "Unknown");
        }

        return Ok(new PagedResult<MovementResponse>
        {
            Items = movements,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        });
    }

    [HttpGet("products/{productId:int}/movements")]
    public async Task<IActionResult> GetProductMovements(int productId)
    {
        var productExists = await _db.Products.AnyAsync(p => p.Id == productId);
        if (!productExists) return NotFound(new { message = "Product not found." });

        return await GetMovements(productId, null, null, null, 1, MaxPageSize);
    }
}