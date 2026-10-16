using System.ComponentModel.DataAnnotations;
using ToolShop.Domain.Entities.Payments;
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

    // Filled when the order has at least one return (PartiallyReturned / Returned). Latest return + totals.
    public DateTime? ReturnedAt { get; set; }
    public string? ReturnedByUsername { get; set; }
    public string? ReturnReason { get; set; }
    public decimal? RefundedAmount { get; set; }

    /// <summary>Value of all returned goods (after order-discount allocation).</summary>
    public decimal ReturnedValue { get; set; }
    public List<OrderReturnResponse> Returns { get; set; } = [];
}

public class OrderReturnItemResponse
{
    public int OrderItemId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int RestockedQuantity { get; set; }
    public int WrittenOffQuantity { get; set; }
    public decimal NetValue { get; set; }
}

public class OrderReturnResponse
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnedAt { get; set; }
    public string ReturnedByUsername { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public decimal GoodsValue { get; set; }
    public decimal RefundAmount { get; set; }
    public PaymentMethod? RefundMethod { get; set; }
    public List<OrderReturnItemResponse> Items { get; set; } = [];
}

public class ReturnItemRequest
{
    public int OrderItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    public int Quantity { get; set; }

    /// <summary>How many of the returned units are damaged / not sellable. They do NOT go back to stock.</summary>
    [Range(0, int.MaxValue)]
    public int DamagedQuantity { get; set; }
}

public class ReturnOrderRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Lines to return. Empty/null = return everything still left on the order (all units sellable).</summary>
    public List<ReturnItemRequest>? Items { get; set; }

    /// <summary>How the money is paid back. Defaults to the method of the latest payment.</summary>
    public PaymentMethod? RefundMethod { get; set; }

    /// <summary>Money actually paid back. Defaults to everything the customer has overpaid after this return.</summary>
    [Range(0, double.MaxValue)]
    public decimal? RefundAmount { get; set; }
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