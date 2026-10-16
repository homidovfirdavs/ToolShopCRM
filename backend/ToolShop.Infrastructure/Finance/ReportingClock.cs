namespace ToolShop.Infrastructure.Finance;

/// <summary>
/// F2: the shop works in local time (Tajikistan, UTC+5, no daylight saving), the database stores UTC instants.
/// A report "day" is therefore a local day. A fixed offset is used on purpose: it needs no tz database in the container.
/// </summary>
public static class ReportingClock
{
    public const int DefaultUtcOffsetHours = 5;

    private static volatile int _offsetHours = DefaultUtcOffsetHours;

    public static TimeSpan Offset => TimeSpan.FromHours(_offsetHours);

    public static void Configure(int utcOffsetHours)
    {
        if (utcOffsetHours < -12 || utcOffsetHours > 14)
            throw new ArgumentOutOfRangeException(nameof(utcOffsetHours), "Reporting:UtcOffsetHours must be between -12 and 14.");
        _offsetHours = utcOffsetHours;
    }

    /// <summary>A UTC instant as local wall-clock time (Kind unspecified, only meant for .Date / .Year / .Month).</summary>
    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) + Offset;

    /// <summary>Today's date in the shop.</summary>
    public static DateTime LocalToday() => ToLocal(DateTime.UtcNow).Date;

    /// <summary>The UTC instant at which the given local calendar day starts.</summary>
    public static DateTime StartOfLocalDayUtc(DateTime localDate) =>
        DateTime.SpecifyKind(localDate.Date - Offset, DateTimeKind.Utc);
}
