namespace ToolShop.Domain.Entities.Purchases;

public class Purchase
{
    public int Id { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;
    public DateTime PurchaseDate { get; set; }
    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;
    public string? ConfirmedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public List<PurchaseItem> Items { get; set; } = [];
}