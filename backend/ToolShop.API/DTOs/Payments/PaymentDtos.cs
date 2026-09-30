using System.ComponentModel.DataAnnotations;
using ToolShop.Domain.Entities.Payments;

namespace ToolShop.API.DTOs.Payments;

public class CreatePaymentRequest
{
    [Required]
    public int OrderId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required]
    public PaymentMethod Method { get; set; }

    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public class VoidPaymentRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public enum OrderPaymentState
{
    Unpaid,
    PartiallyPaid,
    Paid
}

public class PaymentResponse
{
    public int Id { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = "Walk-in Customer";
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public string ReceivedByUsername { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidedByUsername { get; set; }
    public string? VoidReason { get; set; }

    // Snapshot of the order's payment state right after this operation.
    public decimal OrderTotal { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal OutstandingBalance { get; set; }
    public OrderPaymentState PaymentState { get; set; }
}

public class PaymentListItemResponse
{
    public int Id { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = "Walk-in Customer";
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public string ReceivedByUsername { get; set; } = string.Empty;
}

public class OrderPaymentSummaryResponse
{
    public decimal OrderTotal { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal OutstandingBalance { get; set; }
    public OrderPaymentState PaymentState { get; set; }
    public List<PaymentListItemResponse> Payments { get; set; } = [];
}

public class OutstandingOrderResponse
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal Outstanding { get; set; }
    public OrderPaymentState PaymentState { get; set; }
}