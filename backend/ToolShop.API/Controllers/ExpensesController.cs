using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Finance;
using ToolShop.Domain.Authorization;
using ToolShop.Domain.Entities;
using ToolShop.Infrastructure.Finance;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Controllers;

[ApiController]
[Route("api/expenses")]
[Authorize]
public class ExpensesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ExpenseService _expenseService;

    public ExpensesController(AppDbContext db, ExpenseService expenseService)
    {
        _db = db;
        _expenseService = expenseService;
    }

    private string CurrentUserId =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

    [HttpGet]
    [Authorize(Policy = Permissions.ExpensesView)]
    public async Task<ActionResult<FinancePagedResult<ExpenseListItemResponse>>> GetAll(
        [FromQuery] string? search, [FromQuery] int? categoryId,
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate,
        [FromQuery] bool? isVoided, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.Expenses.AsNoTracking()
            .Include(e => e.ExpenseCategory)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e =>
                e.ExpenseNumber.Contains(term) ||
                e.Description.Contains(term) ||
                (e.ReferenceNumber != null && e.ReferenceNumber.Contains(term)));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(e => e.ExpenseCategoryId == categoryId.Value);
        }

        if (fromDate.HasValue)
        {
            var from = DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(e => e.ExpenseDate >= from);
        }

        if (toDate.HasValue)
        {
            var to = DateTime.SpecifyKind(toDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(e => e.ExpenseDate < to);
        }

        if (isVoided.HasValue)
        {
            query = query.Where(e => e.IsVoided == isVoided.Value);
        }

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var userIds = await query
            .OrderByDescending(e => e.ExpenseDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => e.CreatedByUserId)
            .ToListAsync();

        var items = await query
            .OrderByDescending(e => e.ExpenseDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id, e.ExpenseNumber, e.ExpenseDate, CategoryName = e.ExpenseCategory.Name,
                e.Description, e.Amount, e.IsVoided, e.CreatedByUserId
            })
            .ToListAsync();

        var usernames = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Id);

        var result = items.Select(e => new ExpenseListItemResponse(
            e.Id, e.ExpenseNumber, e.ExpenseDate, e.CategoryName, e.Description, e.Amount,
            e.IsVoided, usernames.GetValueOrDefault(e.CreatedByUserId, e.CreatedByUserId)
        )).ToList();

        return Ok(new FinancePagedResult<ExpenseListItemResponse>(result, page, pageSize, totalItems, totalPages));
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.ExpensesView)]
    public async Task<ActionResult<ExpenseResponse>> GetById(int id)
    {
        var expense = await _db.Expenses
            .Include(e => e.ExpenseCategory)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (expense is null)
        {
            return NotFound(new { message = "Expense not found." });
        }

        return Ok(await ToResponseAsync(expense));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.ExpensesCreate)]
    public async Task<ActionResult<ExpenseResponse>> Create([FromBody] CreateExpenseRequest request)
    {
        try
        {
            var expenseDate = DateTime.SpecifyKind(request.ExpenseDate, DateTimeKind.Utc);
            var expense = await _expenseService.CreateAsync(
                request.CategoryId, request.Amount, expenseDate, request.Description,
                request.ReferenceNumber, request.Notes, CurrentUserId);

            var full = await _db.Expenses.Include(e => e.ExpenseCategory).FirstAsync(e => e.Id == expense.Id);
            return Ok(await ToResponseAsync(full));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.ExpensesUpdate)]
    public async Task<ActionResult<ExpenseResponse>> Update(int id, [FromBody] UpdateExpenseRequest request)
    {
        try
        {
            var expenseDate = DateTime.SpecifyKind(request.ExpenseDate, DateTimeKind.Utc);
            var expense = await _expenseService.UpdateAsync(
                id, request.CategoryId, request.Amount, expenseDate, request.Description,
                request.ReferenceNumber, request.Notes, CurrentUserId);

            var full = await _db.Expenses.Include(e => e.ExpenseCategory).FirstAsync(e => e.Id == expense.Id);
            return Ok(await ToResponseAsync(full));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ExpenseVoidedCannotEditException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/void")]
    [Authorize(Policy = Permissions.ExpensesVoid)]
    public async Task<ActionResult<ExpenseResponse>> Void(int id, [FromBody] VoidExpenseRequest request)
    {
        try
        {
            var expense = await _expenseService.VoidAsync(id, request.Reason, CurrentUserId);
            var full = await _db.Expenses.Include(e => e.ExpenseCategory).FirstAsync(e => e.Id == expense.Id);
            return Ok(await ToResponseAsync(full));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ExpenseAlreadyVoidedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private async Task<ExpenseResponse> ToResponseAsync(Domain.Entities.Finance.Expense e)
    {
        var createdByName = await GetUsernameAsync(e.CreatedByUserId);
        var voidedByName = e.VoidedByUserId is null ? null : await GetUsernameAsync(e.VoidedByUserId);

        return new ExpenseResponse(
            e.Id, e.ExpenseNumber, e.ExpenseCategoryId, e.ExpenseCategory.Name,
            e.Amount, e.ExpenseDate, e.Description, e.ReferenceNumber, e.Notes,
            createdByName, e.CreatedAt, e.UpdatedAt,
            e.IsVoided, e.VoidedAt, voidedByName, e.VoidReason);
    }

    private async Task<string> GetUsernameAsync(string userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        return user?.UserName ?? userId;
    }
}