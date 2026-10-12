using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Finance;
using ToolShop.Domain.Authorization;
using ToolShop.Domain.Entities.Finance;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/expense-categories")]
[Authorize]
public class ExpenseCategoriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public ExpenseCategoriesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ExpensesView)]
    public async Task<ActionResult<List<ExpenseCategoryResponse>>> GetAll([FromQuery] bool includeInactive = false)
    {
        var query = _db.ExpenseCategories.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        var categories = await query
            .OrderBy(c => c.Name)
            .Select(c => new ExpenseCategoryResponse(c.Id, c.Name, c.Description, c.IsActive, c.CreatedAt))
            .ToListAsync();

        return Ok(categories);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.ExpensesCreate)]
    public async Task<ActionResult<ExpenseCategoryResponse>> Create([FromBody] ExpenseCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Category name is required." });
        }

        var exists = await _db.ExpenseCategories.AnyAsync(c => c.Name == request.Name);
        if (exists)
        {
            return BadRequest(new { message = "A category with this name already exists." });
        }

        var category = new ExpenseCategory
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.ExpenseCategories.Add(category);
        await _db.SaveChangesAsync();

        return Ok(new ExpenseCategoryResponse(category.Id, category.Name, category.Description, category.IsActive, category.CreatedAt));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.ExpensesUpdate)]
    public async Task<ActionResult<ExpenseCategoryResponse>> Update(int id, [FromBody] ExpenseCategoryRequest request)
    {
        var category = await _db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
        {
            return NotFound(new { message = "Category not found." });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Category name is required." });
        }

        category.Name = request.Name.Trim();
        category.Description = request.Description;
        category.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new ExpenseCategoryResponse(category.Id, category.Name, category.Description, category.IsActive, category.CreatedAt));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permissions.ExpensesUpdate)]
    public async Task<IActionResult> Deactivate(int id)
    {
        var category = await _db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
        {
            return NotFound(new { message = "Category not found." });
        }

        category.IsActive = false;
        category.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return NoContent();
    }
}