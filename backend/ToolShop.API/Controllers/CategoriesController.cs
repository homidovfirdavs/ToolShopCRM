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
[Route("api/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _auditService;

    public CategoriesController(AppDbContext db, AuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    private static CategoryResponse ToResponse(Category c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        NameTj = c.NameTj,
        NameRu = c.NameRu,
        NameEn = c.NameEn,
        Description = c.Description,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };

    /// <summary>Optional translation: trimmed, empty becomes null (display falls back to Name).</summary>
    private static string? CleanTranslation(string? value)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ProductsView)]
    public async Task<IActionResult> GetAll()
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => ToResponse(c))
            .ToListAsync();

        // Real product counts: active products (what Inventory/POS show) and all products incl. inactive.
        var counts = await _db.Products
            .AsNoTracking()
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Active = g.Count(p => p.IsActive), Total = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId);

        return Ok(categories.Select(c => new
        {
            c.Id,
            c.Name,
            c.NameTj,
            c.NameRu,
            c.NameEn,
            c.Description,
            c.IsActive,
            c.CreatedAt,
            c.UpdatedAt,
            productCount = counts.TryGetValue(c.Id, out var n) ? n.Active : 0,
            totalProductCount = counts.TryGetValue(c.Id, out var n2) ? n2.Total : 0
        }));
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.ProductsView)]
    public async Task<IActionResult> GetById(int id)
    {
        var category = await _db.Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category is null)
        {
            return NotFound(new { message = "Category not found." });
        }

        return Ok(ToResponse(category));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.CategoriesManage)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var name = request.Name.Trim();

        var exists = await _db.Categories.AnyAsync(c => c.Name == name);
        if (exists)
        {
            return Conflict(new { message = "A category with this name already exists." });
        }

        var category = new Category
        {
            Name = name,
            NameTj = CleanTranslation(request.NameTj),
            NameRu = CleanTranslation(request.NameRu),
            NameEn = CleanTranslation(request.NameEn),
            Description = request.Description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        var userId = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        await _auditService.LogAsync(userId, "Category.Create", "Category", category.Id.ToString(),
            $"Category '{category.Name}' created.");

        return CreatedAtAction(nameof(GetById), new { id = category.Id }, ToResponse(category));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.CategoriesManage)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
        {
            return NotFound(new { message = "Category not found." });
        }

        var name = request.Name.Trim();

        var duplicate = await _db.Categories.AnyAsync(c => c.Name == name && c.Id != id);
        if (duplicate)
        {
            return Conflict(new { message = "A category with this name already exists." });
        }

        category.Name = name;
        category.NameTj = CleanTranslation(request.NameTj);
        category.NameRu = CleanTranslation(request.NameRu);
        category.NameEn = CleanTranslation(request.NameEn);
        category.Description = request.Description;
        category.IsActive = request.IsActive;
        category.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userId = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        await _auditService.LogAsync(userId, "Category.Update", "Category", category.Id.ToString(),
            $"Category '{category.Name}' updated.");

        return Ok(ToResponse(category));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permissions.CategoriesManage)]
    public async Task<IActionResult> Deactivate(int id)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
        {
            return NotFound(new { message = "Category not found." });
        }

        category.IsActive = false;
        category.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userId = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        await _auditService.LogAsync(userId, "Category.Deactivate", "Category", category.Id.ToString(),
            $"Category '{category.Name}' deactivated.");

        return NoContent();
    }
}