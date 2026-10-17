namespace ToolShop.Infrastructure.Inventory;

public enum CostEventKind
{
    /// <summary>Units come in with a KNOWN unit cost (a purchase, a returned unit whose sale cost is known). A known cost of 0 is a real 0.</summary>
    KnownIn,

    /// <summary>Units come in and their cost was never recorded (stock-in, adjustment up, a return of a sale with unknown cost).</summary>
    UnknownIn,

    /// <summary>Units leave (sale, stock-out, adjustment down, purchase return). They leave at the average, so the average does not change.</summary>
    Out
}

public readonly record struct CostEvent(CostEventKind Kind, int Quantity, decimal UnitCost = 0m);

/// <summary>
/// F3a - moving weighted average cost of the stock, replayed from the inventory movements (oldest first).
///   * a purchase of q units at c:   average = (stock x average + q x c) / (stock + q)
///   * units that leave:             the average stays the same
///   * units whose cost is unknown:  the cost of the stock becomes UNKNOWN (null) - never an estimate, never 0 - and it stays unknown
///                                   until the stock is sold out (units are not tracked one by one, so it cannot be known which
///                                   units were sold). After that the calculation starts again from zero.
/// The result is the cost of ONE unit of the stock as it is now, rounded to 2 decimals (the precision of CostAtSale), or null.
/// </summary>
public static class MovingAverageCost
{
    public static decimal? Compute(IEnumerable<CostEvent> events)
    {
        var stock = 0;
        var knownTotal = 0m;
        var unknown = false;

        foreach (var e in events)
        {
            if (e.Quantity <= 0) continue;

            switch (e.Kind)
            {
                case CostEventKind.KnownIn:
                    if (!unknown) knownTotal += e.Quantity * e.UnitCost;
                    stock += e.Quantity;
                    break;

                case CostEventKind.UnknownIn:
                    unknown = true;
                    stock += e.Quantity;
                    break;

                case CostEventKind.Out:
                    if (!unknown && stock > 0)
                    {
                        var average = knownTotal / stock;
                        knownTotal = average * Math.Max(0, stock - e.Quantity);
                    }
                    stock -= e.Quantity;
                    break;
            }

            if (stock <= 0)
            {
                // sold out: the next purchase starts a clean calculation
                stock = 0;
                knownTotal = 0m;
                unknown = false;
            }
        }

        if (stock <= 0 || unknown) return null;
        return Math.Round(knownTotal / stock, 2, MidpointRounding.AwayFromZero);
    }
}
