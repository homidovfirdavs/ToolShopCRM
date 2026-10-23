namespace ToolShop.Domain.Entities.Cash;

/// <summary>A physical cash drawer. Deactivated, never deleted. The shop has one today; the table lets a second one be added later.</summary>
public class CashRegister
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
