using ToolShop.Infrastructure.Inventory;

namespace ToolShop.Tests.Tests;

/// <summary>F3a - the pure moving-average calculator (no database).</summary>
public class MovingAverageCostTests
{
    private static CostEvent Buy(int qty, decimal cost) => new(CostEventKind.KnownIn, qty, cost);
    private static CostEvent Unknown(int qty) => new(CostEventKind.UnknownIn, qty);
    private static CostEvent Out(int qty) => new(CostEventKind.Out, qty);

    [Fact]
    public void No_movements_means_no_cost()
    {
        Assert.Null(MovingAverageCost.Compute([]));
    }

    [Fact]
    public void Two_purchases_are_averaged_by_quantity()
    {
        Assert.Equal(60m, MovingAverageCost.Compute([Buy(10, 50m), Buy(10, 70m)]));
        Assert.Equal(65m, MovingAverageCost.Compute([Buy(10, 50m), Buy(30, 70m)]));
    }

    [Fact]
    public void Units_leaving_do_not_change_the_average()
    {
        Assert.Equal(60m, MovingAverageCost.Compute([Buy(10, 50m), Buy(10, 70m), Out(5), Out(3)]));
    }

    [Fact]
    public void A_later_purchase_is_weighted_with_what_is_left()
    {
        // 20 units at 60, 6 sold -> 14 at 60; 5 more at 90 -> (14x60 + 5x90) / 19 = 67.89
        Assert.Equal(67.89m, MovingAverageCost.Compute([Buy(10, 50m), Buy(10, 70m), Out(6), Buy(5, 90m)]));
    }

    [Fact]
    public void A_known_cost_of_zero_is_a_real_zero_not_unknown()
    {
        Assert.Equal(0m, MovingAverageCost.Compute([Buy(10, 0m)]));
    }

    [Fact]
    public void Units_with_an_unrecorded_cost_make_the_cost_unknown_never_zero()
    {
        Assert.Null(MovingAverageCost.Compute([Unknown(10)]));
        Assert.Null(MovingAverageCost.Compute([Unknown(5), Buy(5, 50m)]));
        Assert.Null(MovingAverageCost.Compute([Buy(5, 50m), Unknown(5)]));
        Assert.Null(MovingAverageCost.Compute([Unknown(10), Out(3), Buy(5, 50m)]));
    }

    [Fact]
    public void Unknown_stays_until_the_stock_is_sold_out_then_the_calculation_starts_again()
    {
        Assert.Equal(50m, MovingAverageCost.Compute([Unknown(2), Out(2), Buy(10, 50m)]));
        Assert.Equal(70m, MovingAverageCost.Compute([Buy(10, 50m), Out(10), Unknown(3), Out(3), Buy(4, 70m)]));
    }

    [Fact]
    public void The_result_is_rounded_to_cents()
    {
        Assert.Equal(100.33m, MovingAverageCost.Compute([Buy(1, 100m), Buy(1, 100m), Buy(1, 101m)]));
    }

    [Fact]
    public void Selling_more_than_the_stock_never_produces_a_cost()
    {
        Assert.Null(MovingAverageCost.Compute([Buy(2, 50m), Out(5)]));
    }

    [Fact]
    public void Events_with_no_quantity_are_ignored()
    {
        Assert.Equal(50m, MovingAverageCost.Compute([Buy(10, 50m), Unknown(0), Buy(0, 999m)]));
    }
}
