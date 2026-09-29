using System.ComponentModel.DataAnnotations;
using ToolShop.Domain.Entities.Inventory;

namespace ToolShop.API.DTOs.Inventory;

public class StockMutationRequest
{
    [Required]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    public int Quantity { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public class AdjustmentRequest
{
    [Required]
    public int ProductId { get; set; }

    [Required]
    public InventoryMovementType Type { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    public int Quantity { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public enum StockStatus
{
    InStock,
    LowStock,
    OutOfStock
}

public class StockItemResponse
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public int CurrentStock { get; set; }
    public int MinimumStockLevel { get; set; }
    public StockStatus Status { get; set; }
}

public class MovementResponse
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public InventoryMovementType MovementType { get; set; }
    public int Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByUsername { get; set; } = string.Empty;
}