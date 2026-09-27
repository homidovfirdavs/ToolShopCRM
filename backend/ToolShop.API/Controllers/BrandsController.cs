using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Catalog;
using ToolShop.Domain.Entities.Catalog;
using ToolShop.Infrastructure.Audit;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/brands")]
[Authorize]
public class BrandsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _auditService;

    public BrandsController(AppDbContext db, AuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    private static BrandResponse ToResponse(Brand b) => new()
    {
        Id = b.Id,
        Name = b.Name,
        Description = b.Description,
        IsActive = b.IsActive,
        CreatedAt = b.CreatedAt,
        UpdatedAt = b.UpdatedAt
    };

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var brands = await _db.Brands
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => ToResponse(b))
            .ToListAsync();

        return Ok(brands);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var brand = await _db.Brands.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id);

        if (brand is null)
        {
            return NotFound(new { message = "Brand not found." });
        }

        return Ok(ToResponse(brand));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Create([FromBody] CreateBrandRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var name = request.Name.Trim();

        var exists = await _db.Brands.AnyAsync(b => b.Name == name);
        if (exists)
        {
            return Conflict(new { message = "A brand with this name already exists." });
        }

        var brand = new Brand
        {
            Name = name,
            Description = request.Description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Brands.Add(brand);
        await _db.SaveChangesAsync();

        var userId = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        await _auditService.LogAsync(userId, "Brand.Create", "Brand", brand.Id.ToString(),
            $"Brand '{brand.Name}' created.");

        return CreatedAtAction(nameof(GetById), new { id = brand.Id }, ToResponse(brand));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateBrandRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == id);
        if (brand is null)
        {
            return NotFound(new { message = "Brand not found." });
        }

        var name = request.Name.Trim();

        var duplicate = await _db.Brands.AnyAsync(b => b.Name == name && b.Id != id);
        if (duplicate)
        {
            return Conflict(new { message = "A brand with this name already exists." });
        }

        brand.Name = name;
        brand.Description = request.Description;
        brand.IsActive = request.IsActive;
        brand.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userId = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        await _auditService.LogAsync(userId, "Brand.Update", "Brand", brand.Id.ToString(),
            $"Brand '{brand.Name}' updated.");

        return Ok(ToResponse(brand));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == id);
        if (brand is null)
        {
            return NotFound(new { message = "Brand not found." });
        }

        brand.IsActive = false;
        brand.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userId = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        await _auditService.LogAsync(userId, "Brand.Deactivate", "Brand", brand.Id.ToString(),
            $"Brand '{brand.Name}' deactivated.");

        return NoContent();
    }
}