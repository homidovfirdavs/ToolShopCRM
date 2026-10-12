using ToolShop.Domain.Entities.Catalog;

namespace ToolShop.Domain.Entities.Sales;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }

    // TASK 09: cost snapshot at the moment of sale completion.
    // Null means the sale was completed before TASK 09, or no cost source was found —
    // Finance reports must treat null as "unknown cost", never as zero.
    public decimal? CostAtSale { get; set; }
}