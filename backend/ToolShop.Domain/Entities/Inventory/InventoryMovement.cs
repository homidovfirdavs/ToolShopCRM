using ToolShop.Domain.Entities.Catalog;

namespace ToolShop.Domain.Entities.Inventory;

public class InventoryMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public InventoryMovementType MovementType { get; set; }
    public int Quantity { get; set; }

    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }

    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}