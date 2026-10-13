namespace ToolShop.Domain.Entities.Catalog;

public class Category
{
    public int Id { get; set; }
    /// <summary>Main name: unique, required, and the fallback when a translation is empty.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional translations (display only). Empty = fall back to <see cref="Name"/>.</summary>
    public string? NameTj { get; set; }
    public string? NameRu { get; set; }
    public string? NameEn { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}