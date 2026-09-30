using System.ComponentModel.DataAnnotations;
using ToolShop.Domain.Entities.Sales;

namespace ToolShop.API.DTOs.Sales;

public class OrderItemRequest
{
    [Required]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    public int Quantity { get; set; }

    [Range(0, double.MaxValue)]
    public decimal DiscountAmount { get; set; }
}

public class CreateOrderRequest
{
    public int? CustomerId { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [Range(0, double.MaxValue)]
    public decimal DiscountAmount { get; set; }

    [MinLength(1, ErrorMessage = "At least one item is required.")]
    public List<OrderItemRequest> Items { get; set; } = [];
}

public class UpdateOrderRequest
{
    public int? CustomerId { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [Range(0, double.MaxValue)]
    public decimal DiscountAmount { get; set; }

    [MinLength(1, ErrorMessage = "At least one item is required.")]
    public List<OrderItemRequest> Items { get; set; } = [];
}

public class OrderItemResponse
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class OrderResponse
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = "Walk-in Customer";
    public OrderStatus Status { get; set; }
    public DateTime OrderDate { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public List<OrderItemResponse> Items { get; set; } = [];
    public string CreatedByUsername { get; set; } = string.Empty;
    public string? CompletedByUsername { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public class OrderListItemResponse
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = "Walk-in Customer";
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public string CreatedByUsername { get; set; } = string.Empty;
}