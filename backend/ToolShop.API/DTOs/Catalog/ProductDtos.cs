using System.ComponentModel.DataAnnotations;

namespace ToolShop.API.DTOs.Catalog;

public class CreateProductRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string SKU { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Barcode { get; set; }

    [Required]
    public int CategoryId { get; set; }

    [Required]
    public int BrandId { get; set; }

    [Range(0, double.MaxValue)]
    public decimal PurchasePrice { get; set; }

    [Range(0, double.MaxValue)]
    public decimal SalePrice { get; set; }

    [Range(0, int.MaxValue)]
    public int MinimumStockLevel { get; set; }

    [Range(0, int.MaxValue)]
    public int WarrantyMonths { get; set; }

    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
}

public class UpdateProductRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string SKU { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Barcode { get; set; }

    [Required]
    public int CategoryId { get; set; }

    [Required]
    public int BrandId { get; set; }

    [Range(0, double.MaxValue)]
    public decimal PurchasePrice { get; set; }

    [Range(0, double.MaxValue)]
    public decimal SalePrice { get; set; }

    [Range(0, int.MaxValue)]
    public int MinimumStockLevel { get; set; }

    [Range(0, int.MaxValue)]
    public int WarrantyMonths { get; set; }

    public string? Description { get; set; }
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; }
}

public class ProductResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? Barcode { get; set; }

    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;

    public int BrandId { get; set; }
    public string BrandName { get; set; } = string.Empty;

    public decimal? PurchasePrice { get; set; }  // null = hidden for this role (not 0)
    public decimal SalePrice { get; set; }
    public int MinimumStockLevel { get; set; }
    public int WarrantyMonths { get; set; }

    public string? Description { get; set; }
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ProductListItemResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public decimal? PurchasePrice { get; set; }  // null = hidden for this role (not 0)
    public decimal SalePrice { get; set; }
    public bool IsActive { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}