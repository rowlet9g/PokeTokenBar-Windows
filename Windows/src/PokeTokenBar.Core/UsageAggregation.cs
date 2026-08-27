using System.Globalization;

namespace PokeTokenBar.Core;

public static class UsageAggregation
{
    public static readonly TimeSpan BlockWindow = TimeSpan.FromHours(5);

    public static DailyUsage? Daily(IEnumerable<UsageEntry> entries, DateOnly localDay)
    {
        var bucket = UsageBucket.Empty;
        foreach (var entry in entries)
        {
            if (entry.LocalDay == localDay)
            {
                bucket = bucket.Add(entry);
            }
        }

        if (bucket.Total == 0)
        {
            return null;
        }

        return new DailyUsage(
            localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bucket.Input,
            bucket.Output,
            bucket.CacheWrite,
            bucket.CacheRead,
            bucket.Total,
            bucket.Cost);
    }

    public static PeriodUsage Period(
        IEnumerable<UsageEntry> entries,
        string periodKey,
        DateOnly fromDay,
        DateOnly toDay)
    {
        var bucket = UsageBucket.Empty;
        foreach (var entry in entries)
        {
            if (entry.LocalDay >= fromDay && entry.LocalDay <= toDay)
            {
                bucket = bucket.Add(entry);
            }
        }

        return new PeriodUsage(periodKey, bucket.Total, bucket.Cost);
    }

    public static BlockUsage? ActiveBlock(
        IEnumerable<UsageEntry> entries,
        DateTimeOffset now)
    {
        var windowStart = now - BlockWindow;
        var recent = entries
            .Where(entry => entry.Timestamp >= windowStart)
            .OrderBy(entry => entry.Timestamp)
            .ToArray();
        if (recent.Length == 0)
        {
            return null;
        }

        var bucket = recent.Aggregate(UsageBucket.Empty, (current, entry) => current.Add(entry));
        var first = recent[0].Timestamp;
        var minutes = Math.Max(1, (now - first).TotalMinutes);

        return new BlockUsage(
            $"block-{first.ToUnixTimeSeconds()}",
            first,
            first + BlockWindow,
            IsActive: true,
            bucket.Total,
            bucket.Cost,
            bucket.Total / minutes);
    }

    public static DateOnly StartOfWeek(DateOnly date, DayOfWeek firstDayOfWeek)
    {
        var delta = (7 + (int)date.DayOfWeek - (int)firstDayOfWeek) % 7;
        return date.AddDays(-delta);
    }

    public static DateTimeOffset EnrichmentScanStart(DateTimeOffset now)
    {
        var localNow = now.ToLocalTime();
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var weekStart = StartOfWeek(today, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
        var monthInstant = AtLocalMidnight(monthStart, localNow.Offset);
        var weekInstant = AtLocalMidnight(weekStart, localNow.Offset);
        var blockInstant = now - BlockWindow;
        return new[] { monthInstant, weekInstant, blockInstant }.Min();
    }

    public static string MonthKey(DateOnly date) =>
        date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static DateTimeOffset AtLocalMidnight(DateOnly date, TimeSpan fallbackOffset)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        try
        {
            return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }
        catch (ArgumentException)
        {
            return new DateTimeOffset(local, fallbackOffset);
        }
    }

    private readonly record struct UsageBucket(
        long Input,
        long Output,
        long CacheWrite,
        long CacheRead,
        double Cost)
    {
        public static UsageBucket Empty => new(0, 0, 0, 0, 0);

        public long Total => UsageMath.SaturatingSum(Input, Output, CacheWrite, CacheRead);

        public UsageBucket Add(UsageEntry entry) => new(
            UsageMath.SaturatingAdd(Input, entry.Input),
            UsageMath.SaturatingAdd(Output, entry.Output),
            UsageMath.SaturatingAdd(CacheWrite, entry.CacheWrite),
            UsageMath.SaturatingAdd(CacheRead, entry.CacheRead),
            Cost + (entry.ExplicitCost is > 0 ? entry.ExplicitCost.Value : 0));
    }
}

public static class UsageMath
{
    public static long SaturatingAdd(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right)
        {
            return long.MaxValue;
        }

        if (right < 0 && left < long.MinValue - right)
        {
            return long.MinValue;
        }

        return left + right;
    }

    public static long SaturatingSum(params long[] values) =>
        values.Aggregate(0L, SaturatingAdd);
}
