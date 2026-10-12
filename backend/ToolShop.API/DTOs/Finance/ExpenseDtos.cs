namespace ToolShop.API.DTOs.Finance;

public record ExpenseCategoryRequest(string Name, string? Description);

public record ExpenseCategoryResponse(
    int Id, string Name, string? Description, bool IsActive, DateTime CreatedAt);

public record CreateExpenseRequest(
    int CategoryId, decimal Amount, DateTime ExpenseDate, string Description,
    string? ReferenceNumber, string? Notes);

public record UpdateExpenseRequest(
    int CategoryId, decimal Amount, DateTime ExpenseDate, string Description,
    string? ReferenceNumber, string? Notes);

public record VoidExpenseRequest(string Reason);

public record ExpenseResponse(
    int Id, string ExpenseNumber, int CategoryId, string CategoryName,
    decimal Amount, DateTime ExpenseDate, string Description,
    string? ReferenceNumber, string? Notes,
    string CreatedByUsername, DateTime CreatedAt, DateTime? UpdatedAt,
    bool IsVoided, DateTime? VoidedAt, string? VoidedByUsername, string? VoidReason);

public record ExpenseListItemResponse(
    int Id, string ExpenseNumber, DateTime ExpenseDate, string CategoryName,
    string Description, decimal Amount, bool IsVoided, string CreatedByUsername);

public record FinancePagedResult<T>(
    List<T> Items, int Page, int PageSize, int TotalItems, int TotalPages);