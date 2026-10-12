namespace ToolShop.Domain.Entities.Finance;

public class Expense
{
    public int Id { get; set; }
    public string ExpenseNumber { get; set; } = string.Empty;

    public int ExpenseCategoryId { get; set; }
    public ExpenseCategory ExpenseCategory { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateTime ExpenseDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public bool IsVoided { get; set; } = false;
    public DateTime? VoidedAt { get; set; }
    public string? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}