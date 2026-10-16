using ToolShop.Domain.Entities.Payments;

namespace ToolShop.Domain.Entities.Sales;

/// <summary>
/// One return operation against a sold order (full or partial). This is an immutable LEDGER row: it is never updated
/// or deleted - a mistake is corrected by a new operation, not by editing history. The return is dated by
/// <see cref="ReturnedAt"/> (server time, UTC) and is accounted in the period in which it happened; the original
/// sale stays in its own period.
/// </summary>
public class OrderReturn
{
    public int Id { get; set; }

    /// <summary>RET-000001 ... generated from the return_number_seq sequence.</summary>
    public string ReturnNumber { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>When the return really happened (server UTC time; never entered by the user).</summary>
    public DateTime ReturnedAt { get; set; }

    public string ReturnedByUserId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    /// <summary>Value of the returned goods after the order discount was allocated pro-rata (reduces Revenue).</summary>
    public decimal GoodsValue { get; set; }

    /// <summary>Cost of the RESTOCKED units (CostAtSale x quantity) that goes back out of COGS. Null = some cost unknown.</summary>
    public decimal? CostReversed { get; set; }

    /// <summary>Cost of the WRITTEN-OFF units. Memo only: it stays inside COGS (never subtracted or added again). Null = unknown.</summary>
    public decimal? WriteOffCost { get; set; }

    /// <summary>Money actually paid back to the customer (cash outflow in the period of the return).</summary>
    public decimal RefundAmount { get; set; }

    /// <summary>How the money was paid back. Required when RefundAmount &gt; 0.</summary>
    public PaymentMethod? RefundMethod { get; set; }

    public List<OrderReturnItem> Items { get; set; } = [];
}
