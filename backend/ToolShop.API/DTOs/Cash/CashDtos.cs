using System.ComponentModel.DataAnnotations;
using ToolShop.Domain.Entities.Cash;

namespace ToolShop.API.DTOs.Cash;

public class OpenCashShiftRequest
{
    /// <summary>Cash counted in the drawer at the start. Required (an empty body must not open a shift).</summary>
    [Required]
    [Range(0, 10_000_000)]
    public decimal? OpeningCash { get; set; }

    /// <summary>Optional while the shop has one drawer.</summary>
    public int? CashRegisterId { get; set; }
}

public class CloseCashShiftRequest
{
    /// <summary>Cash actually counted at closing. Required.</summary>
    [Required]
    [Range(0, 10_000_000)]
    public decimal? ActualCash { get; set; }

    /// <summary>Required when the counted cash differs from the expected cash.</summary>
    [MaxLength(1000)]
    public string? Note { get; set; }
}

public class CashMovementRequest
{
    [Required]
    public CashMovementType? Type { get; set; }

    [Required]
    [Range(0.01, 10_000_000)]
    public decimal? Amount { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public int? ExpenseId { get; set; }
}

public class VoidCashMovementRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class ReviewCashShiftRequest
{
    [MaxLength(1000)]
    public string? Note { get; set; }
}
