namespace ToolShop.Domain.Entities.Catalog;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? Barcode { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;

    public decimal PurchasePrice { get; set; }
    public decimal SalePrice { get; set; }

    public int MinimumStockLevel { get; set; }
    public int WarrantyMonths { get; set; }

    public string? Description { get; set; }
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}