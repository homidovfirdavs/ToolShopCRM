using System.ComponentModel.DataAnnotations;
using ToolShop.Domain.Entities.Purchases;

namespace ToolShop.API.DTOs.Purchases;

public class PurchaseItemRequest
{
    [Required]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    public int Quantity { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Unit cost must be 0 or more.")]
    public decimal UnitCost { get; set; }
}

public class CreatePurchaseRequest
{
    [Required]
    public int SupplierId { get; set; }

    [Required]
    public DateTime PurchaseDate { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [Range(0, double.MaxValue)]
    public decimal DiscountAmount { get; set; }

    [MinLength(1, ErrorMessage = "At least one item is required.")]
    public List<PurchaseItemRequest> Items { get; set; } = [];
}

public class UpdatePurchaseRequest
{
    [Required]
    public int SupplierId { get; set; }

    [Required]
    public DateTime PurchaseDate { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [Range(0, double.MaxValue)]
    public decimal DiscountAmount { get; set; }

    [MinLength(1, ErrorMessage = "At least one item is required.")]
    public List<PurchaseItemRequest> Items { get; set; } = [];
}

public class PurchaseItemResponse
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
}

public class PurchaseResponse
{
    public int Id { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public PurchaseStatus Status { get; set; }
    public DateTime PurchaseDate { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public List<PurchaseItemResponse> Items { get; set; } = [];
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByUsername { get; set; } = string.Empty;
    public string? ConfirmedByUserId { get; set; }
    public string? ConfirmedByUsername { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public class PurchaseListItemResponse
{
    public int Id { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; }
    public PurchaseStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public string CreatedByUsername { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}