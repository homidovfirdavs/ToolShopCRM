using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Catalog;
using ToolShop.Domain.Authorization;
using ToolShop.Domain.Entities.Catalog;
using ToolShop.Infrastructure.Audit;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _auditService;
    private const int MaxPageSize = 100;

    public ProductsController(AppDbContext db, AuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    private static ProductResponse ToResponse(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        SKU = p.SKU,
        Barcode = p.Barcode,
        CategoryId = p.CategoryId,
        CategoryName = p.Category.Name,
        BrandId = p.BrandId,
        BrandName = p.Brand.Name,
        PurchasePrice = p.PurchasePrice,
        SalePrice = p.SalePrice,
        MinimumStockLevel = p.MinimumStockLevel,
        WarrantyMonths = p.WarrantyMonths,
        Description = p.Description,
        ImageUrl = p.ImageUrl,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };

    private static ProductListItemResponse ToListItem(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        SKU = p.SKU,
        Barcode = p.Barcode,
        CategoryName = p.Category.Name,
        BrandName = p.Brand.Name,
        PurchasePrice = p.PurchasePrice,
        SalePrice = p.SalePrice,
        IsActive = p.IsActive
    };

    /// <summary>Purchase (cost) price is confidential: only roles that manage stock/prices see it; others get null.</summary>
    private bool CanSeeCost => User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Warehouse");

    [HttpGet]
    [Authorize(Policy = Permissions.ProductsView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? categoryId,
        [FromQuery] int? brandId,
        [FromQuery] bool? isActive,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var query = _db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.SKU.ToLower().Contains(term) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(term)));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (brandId.HasValue)
        {
            query = query.Where(p => p.BrandId == brandId.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(p => p.IsActive == isActive.Value);
        }

        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortBy?.ToLower() switch
        {
            "saleprice" => descending ? query.OrderByDescending(p => p.SalePrice) : query.OrderBy(p => p.SalePrice),
            "createdat" => descending ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            _ => descending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
        };

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => ToListItem(p))
            .ToListAsync();

        if (!CanSeeCost)
        {
            foreach (var item in items) item.PurchasePrice = null;
        }

        return Ok(new PagedResult<ProductListItemResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        });
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.ProductsView)]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null)
        {
            return NotFound(new { message = "Product not found." });
        }

        var response = ToResponse(product);
        if (!CanSeeCost) response.PurchasePrice = null;
        return Ok(response);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.ProductsCreate)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var sku = request.SKU.Trim();
        var barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();

        var skuExists = await _db.Products.AnyAsync(p => p.SKU == sku);
        if (skuExists)
        {
            return Conflict(new { message = "A product with this SKU already exists." });
        }

        if (barcode is not null)
        {
            var barcodeExists = await _db.Products.AnyAsync(p => p.Barcode == barcode);
            if (barcodeExists)
            {
                return Conflict(new { message = "A product with this barcode already exists." });
            }
        }

        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == request.CategoryId);
        if (!categoryExists)
        {
            return BadRequest(new { message = "Category does not exist." });
        }

        var brandExists = await _db.Brands.AnyAsync(b => b.Id == request.BrandId);
        if (!brandExists)
        {
            return BadRequest(new { message = "Brand does not exist." });
        }

        var product = new Product
        {
            Name = request.Name.Trim(),
            SKU = sku,
            Barcode = barcode,
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            PurchasePrice = request.PurchasePrice,
            SalePrice = request.SalePrice,
            MinimumStockLevel = request.MinimumStockLevel,
            WarrantyMonths = request.WarrantyMonths,
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        await _db.Entry(product).Reference(p => p.Category).LoadAsync();
        await _db.Entry(product).Reference(p => p.Brand).LoadAsync();

        var userId = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        await _auditService.LogAsync(userId, "Product.Create", "Product", product.Id.ToString(),
            $"Product '{product.Name}' (SKU: {product.SKU}) created.");

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ToResponse(product));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.ProductsUpdate)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var product = await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null)
        {
            return NotFound(new { message = "Product not found." });
        }

        var sku = request.SKU.Trim();
        var barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();

        var skuDuplicate = await _db.Products.AnyAsync(p => p.SKU == sku && p.Id != id);
        if (skuDuplicate)
        {
            return Conflict(new { message = "A product with this SKU already exists." });
        }

        if (barcode is not null)
        {
            var barcodeDuplicate = await _db.Products.AnyAsync(p => p.Barcode == barcode && p.Id != id);
            if (barcodeDuplicate)
            {
                return Conflict(new { message = "A product with this barcode already exists." });
            }
        }

        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == request.CategoryId);
        if (!categoryExists)
        {
            return BadRequest(new { message = "Category does not exist." });
        }

        var brandExists = await _db.Brands.AnyAsync(b => b.Id == request.BrandId);
        if (!brandExists)
        {
            return BadRequest(new { message = "Brand does not exist." });
        }

        var oldSalePrice = product.SalePrice;

        product.Name = request.Name.Trim();
        product.SKU = sku;
        product.Barcode = barcode;
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.PurchasePrice = request.PurchasePrice;
        product.SalePrice = request.SalePrice;
        product.MinimumStockLevel = request.MinimumStockLevel;
        product.WarrantyMonths = request.WarrantyMonths;
        product.Description = request.Description;
        product.ImageUrl = request.ImageUrl;
        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _db.Entry(product).Reference(p => p.Category).LoadAsync();
        await _db.Entry(product).Reference(p => p.Brand).LoadAsync();

        var userId = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        await _auditService.LogAsync(userId, "Product.Update", "Product", product.Id.ToString(),
            $"Product '{product.Name}' (SKU: {product.SKU}) updated.");

        if (oldSalePrice != product.SalePrice)
        {
            await _auditService.LogAsync(userId, "Product.PriceChange", "Product", product.Id.ToString(),
                $"SalePrice changed: {oldSalePrice} -> {product.SalePrice}");
        }

        return Ok(ToResponse(product));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permissions.ProductsDelete)]
    public async Task<IActionResult> Deactivate(int id)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null)
        {
            return NotFound(new { message = "Product not found." });
        }

        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userId = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        await _auditService.LogAsync(userId, "Product.Deactivate", "Product", product.Id.ToString(),
            $"Product '{product.Name}' deactivated.");

        return NoContent();
    }
}