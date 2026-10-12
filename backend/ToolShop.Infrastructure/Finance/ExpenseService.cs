using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities;
using ToolShop.Domain.Entities.Finance;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Finance;

public class ExpenseAlreadyVoidedException : Exception
{
    public ExpenseAlreadyVoidedException() : base("This expense has already been voided.") { }
}

public class ExpenseVoidedCannotEditException : Exception
{
    public ExpenseVoidedCannotEditException() : base("A voided expense cannot be edited.") { }
}

public class ExpenseService
{
    private readonly AppDbContext _db;

    public ExpenseService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Expense> CreateAsync(
        int categoryId, decimal amount, DateTime expenseDate, string description,
        string? referenceNumber, string? notes, string userId)
    {
        if (amount <= 0)
        {
            throw new ArgumentException("Amount must be greater than zero.");
        }

        var category = await _db.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.IsActive);

        if (category is null)
        {
            throw new KeyNotFoundException("Expense category not found or inactive.");
        }

        var expenseNumber = await ExpenseNumberGenerator.GenerateAsync(_db);

        var expense = new Expense
        {
            ExpenseNumber = expenseNumber,
            ExpenseCategoryId = categoryId,
            Amount = amount,
            ExpenseDate = expenseDate,
            Description = description,
            ReferenceNumber = referenceNumber,
            Notes = notes,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Expenses.Add(expense);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "ExpenseCreated",
            EntityName = "Expense",
            Description = $"Created expense {expenseNumber} for {amount}",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _db.AuditLogs.Local
            .Last(a => a.Action == "ExpenseCreated" && a.EntityId == null)
            .EntityId = expense.Id.ToString();
        await _db.SaveChangesAsync();

        return expense;
    }

    public async Task<Expense> UpdateAsync(
        int expenseId, int categoryId, decimal amount, DateTime expenseDate,
        string description, string? referenceNumber, string? notes, string userId)
    {
        if (amount <= 0)
        {
            throw new ArgumentException("Amount must be greater than zero.");
        }

        var expense = await _db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId);
        if (expense is null)
        {
            throw new KeyNotFoundException("Expense not found.");
        }

        if (expense.IsVoided)
        {
            throw new ExpenseVoidedCannotEditException();
        }

        var category = await _db.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.IsActive);
        if (category is null)
        {
            throw new KeyNotFoundException("Expense category not found or inactive.");
        }

        expense.ExpenseCategoryId = categoryId;
        expense.Amount = amount;
        expense.ExpenseDate = expenseDate;
        expense.Description = description;
        expense.ReferenceNumber = referenceNumber;
        expense.Notes = notes;
        expense.UpdatedAt = DateTime.UtcNow;

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "ExpenseUpdated",
            EntityName = "Expense",
            EntityId = expense.Id.ToString(),
            Description = $"Updated expense {expense.ExpenseNumber}",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return expense;
    }

    public async Task<Expense> VoidAsync(int expenseId, string reason, string userId)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Void reason is required.");
        }

        var expense = await _db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId);
        if (expense is null)
        {
            throw new KeyNotFoundException("Expense not found.");
        }

        if (expense.IsVoided)
        {
            throw new ExpenseAlreadyVoidedException();
        }

        expense.IsVoided = true;
        expense.VoidedAt = DateTime.UtcNow;
        expense.VoidedByUserId = userId;
        expense.VoidReason = reason.Trim();

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "ExpenseVoided",
            EntityName = "Expense",
            EntityId = expense.Id.ToString(),
            Description = $"Voided expense {expense.ExpenseNumber}: {reason.Trim()}",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return expense;
    }
}