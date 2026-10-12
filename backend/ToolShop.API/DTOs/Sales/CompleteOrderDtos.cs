using System.ComponentModel.DataAnnotations;
using ToolShop.Domain.Entities.Payments;

namespace ToolShop.API.DTOs.Sales;

/// <summary>Optional body for POST /api/orders/{id}/complete (atomic completion + initial payment).</summary>
public class CompleteOrderRequest
{
    public InitialPaymentRequest? InitialPayment { get; set; }
}

public class InitialPaymentRequest
{
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required]
    public PaymentMethod Method { get; set; }

    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}
