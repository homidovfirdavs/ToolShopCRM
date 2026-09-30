using ToolShop.Domain.Entities.Customers;
using ToolShop.Domain.Entities.Sales;

namespace ToolShop.Domain.Entities.Payments;

public class Payment
{
    public int Id { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Completed;

    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }

    public string ReceivedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? VoidedAt { get; set; }
    public string? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}