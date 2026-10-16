using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Infrastructure.Finance;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// F2 - a report "day" is the shop's local day (UTC+5, no DST), not the UTC day.
/// A sale made at 02:00 local on 11 March is stored as 21:00 UTC on 10 March and must still be reported on 11 March.
/// Formulas are not under test here (see ReturnFinanceTests); only WHICH DAY / MONTH a stored instant belongs to.
/// </summary>
[Collection("db")]
public class ReportingTimezoneTests(ApiFactory factory) : TestBase(factory)
{
    private static DateTime Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    /// <summary>One item (price 100, cost 60), paid in full; the sale AND its payment are moved to the given UTC instant.</summary>
    private async Task<int> SaleAtAsync(DateTime utc)
    {
        var product = await SeedProductAsync(salePrice: 100m, purchasePrice: 60m, stock: 20);
        var customer = await SeedCustomerAsync();
        var (id, total) = await CreateOrderAsync(customer, 0m, Item(product, 1));
        await (await CompleteAsync(id, total)).ShouldBeAsync(HttpStatusCode.OK);

        await Factory.WithDbAsync(async db =>
        {
            await db.Orders.Where(o => o.Id == id).ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDate, utc));
            await db.Payments.Where(p => p.OrderId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, utc));
        });
        return id;
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var resp = await Admin.GetAsync(url);
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        return await resp.ReadJsonAsync();
    }

    private static JsonElement Day(JsonElement daily, string date) =>
        daily.EnumerateArray().Single(d => d.Str("date") == date);

    // ---------- the clock itself ----------

    [Fact]
    public void Local_midnight_is_five_hours_before_UTC_midnight()
    {
        Assert.Equal(Utc(2026, 3, 10, 19), ReportingClock.StartOfLocalDayUtc(new DateTime(2026, 3, 11)));
        Assert.Equal(DateTimeKind.Utc, ReportingClock.StartOfLocalDayUtc(new DateTime(2026, 3, 11)).Kind);
    }

    [Fact]
    public void A_UTC_instant_after_19_00_belongs_to_the_next_local_day()
    {
        Assert.Equal(new DateTime(2026, 3, 11), ReportingClock.ToLocal(Utc(2026, 3, 10, 21)).Date);
        Assert.Equal(new DateTime(2026, 3, 10), ReportingClock.ToLocal(Utc(2026, 3, 10, 18, 59)).Date);
        Assert.Equal(new DateTime(2026, 3, 11), ReportingClock.ToLocal(Utc(2026, 3, 10, 19)).Date);
    }

    [Fact]
    public void Offset_must_be_a_real_time_zone_offset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportingClock.Configure(15));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportingClock.Configure(-13));
        Assert.Equal(TimeSpan.FromHours(5), ReportingClock.Offset);
    }

    // ---------- daily ----------

    [Fact]
    public async Task Night_sale_at_02_00_local_is_reported_on_its_local_day()
    {
        await SaleAtAsync(Utc(2026, 3, 10, 21));             // 11 March 02:00 local

        var daily = await GetJsonAsync("/api/finance/daily?from=2026-03-10&to=2026-03-11");
        Assert.Equal(0m, Day(daily, "2026-03-10").Dec("revenue"));
        Assert.Equal(100m, Day(daily, "2026-03-11").Dec("revenue"));
        Assert.Equal(100m, Day(daily, "2026-03-11").Dec("paymentsReceived"));
    }

    [Fact]
    public async Task The_local_day_boundary_is_exactly_19_00_UTC()
    {
        await SaleAtAsync(Utc(2026, 3, 10, 18, 59));         // 10 March 23:59 local
        await SaleAtAsync(Utc(2026, 3, 10, 19, 0));          // 11 March 00:00 local

        var daily = await GetJsonAsync("/api/finance/daily?from=2026-03-10&to=2026-03-11");
        Assert.Equal(100m, Day(daily, "2026-03-10").Dec("revenue"));
        Assert.Equal(100m, Day(daily, "2026-03-11").Dec("revenue"));
        Assert.Equal(2, daily.GetArrayLength());             // exactly the two requested days
    }

    // ---------- summary / reports use the same window ----------

    [Fact]
    public async Task Summary_of_one_local_day_includes_the_night_sale_and_excludes_the_previous_evening()
    {
        await SaleAtAsync(Utc(2026, 3, 10, 18));             // 10 March 23:00 local
        await SaleAtAsync(Utc(2026, 3, 10, 21));             // 11 March 02:00 local

        var march11 = await GetJsonAsync("/api/finance/summary?from=2026-03-11&to=2026-03-11");
        Assert.Equal(100m, march11.Dec("revenue"));
        Assert.Equal(1, march11.Int("completedOrdersCount"));
        Assert.Equal(100m, march11.Dec("paymentsReceived"));

        var march10 = await GetJsonAsync("/api/finance/summary?from=2026-03-10&to=2026-03-10");
        Assert.Equal(100m, march10.Dec("revenue"));
    }

    [Fact]
    public async Task Sales_report_rows_follow_the_local_day()
    {
        await SaleAtAsync(Utc(2026, 3, 10, 21));             // 11 March 02:00 local -> in
        await SaleAtAsync(Utc(2026, 3, 11, 19, 30));         // 12 March 00:30 local -> out

        var report = await GetJsonAsync("/api/reports/sales?from=2026-03-11&to=2026-03-11");
        Assert.Equal(1, report.Int("totalItems"));
    }

    // ---------- monthly ----------

    [Fact]
    public async Task Sale_in_the_last_hours_of_a_UTC_month_belongs_to_the_next_local_month()
    {
        await SaleAtAsync(Utc(2026, 3, 31, 20));             // 1 April 01:00 local

        var monthly = await GetJsonAsync("/api/finance/monthly?from=2026-03-01&to=2026-04-30");
        var row = Assert.Single(monthly.EnumerateArray());
        Assert.Equal(2026, row.Int("year"));
        Assert.Equal(4, row.Int("month"));
        Assert.Equal(100m, row.Dec("revenue"));
    }

    // ---------- "today" ----------

    [Fact]
    public async Task Default_range_ends_on_the_shop_local_today()
    {
        await SaleAtAsync(DateTime.UtcNow);

        var summary = await GetJsonAsync("/api/finance/summary");   // no from/to
        Assert.Equal(100m, summary.Dec("revenue"));
    }
}
