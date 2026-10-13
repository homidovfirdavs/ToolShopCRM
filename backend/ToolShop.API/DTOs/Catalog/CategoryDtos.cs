using System.ComponentModel.DataAnnotations;

namespace ToolShop.API.DTOs.Catalog;

public class CreateCategoryRequest
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? NameTj { get; set; }

    [MaxLength(150)]
    public string? NameRu { get; set; }

    [MaxLength(150)]
    public string? NameEn { get; set; }

    public string? Description { get; set; }
}

public class UpdateCategoryRequest
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? NameTj { get; set; }

    [MaxLength(150)]
    public string? NameRu { get; set; }

    [MaxLength(150)]
    public string? NameEn { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; }
}

public class CategoryResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameTj { get; set; }
    public string? NameRu { get; set; }
    public string? NameEn { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}