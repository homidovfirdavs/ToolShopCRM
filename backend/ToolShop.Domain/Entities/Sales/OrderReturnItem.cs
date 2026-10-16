using ToolShop.Domain.Entities.Catalog;

namespace ToolShop.Domain.Entities.Sales;

/// <summary>One returned order line. Quantity = RestockedQuantity (back to sellable stock) + WrittenOffQuantity (damaged / not sellable).</summary>
public class OrderReturnItem
{
    public int Id { get; set; }

    public int OrderReturnId { get; set; }
    public OrderReturn OrderReturn { get; set; } = null!;

    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }
    public int RestockedQuantity { get; set; }
    public int WrittenOffQuantity { get; set; }

    /// <summary>Net value of the returned quantity (discount allocated). The sum over items equals OrderReturn.GoodsValue.</summary>
    public decimal NetValue { get; set; }

    /// <summary>Copy of OrderItem.CostAtSale at the time of the return. Null = unknown cost (N/A, never 0).</summary>
    public decimal? CostAtSaleSnapshot { get; set; }
}
