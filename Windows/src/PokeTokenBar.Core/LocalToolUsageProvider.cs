using System.Globalization;

namespace PokeTokenBar.Core;

/// <summary>Aggregates local records without assigning API prices to subscriptions.</summary>
public sealed class LocalToolUsageProvider(
    string id,
    string displayName,
    Func<DateTimeOffset, CancellationToken, IReadOnlyList<UsageEntry>> readEntries) : IUsageProvider
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public bool ReportsCost => false;

    public Task<ProviderSnapshot?> FetchAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var entries = readEntries(UsageAggregation.EnrichmentScanStart(now), cancellationToken);
            var day = DateOnly.FromDateTime(now.LocalDateTime);
            var weekStart = UsageAggregation.StartOfWeek(day, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
            var monthStart = new DateOnly(day.Year, day.Month, 1);
            var today = UsageAggregation.Daily(entries, day);
            var week = UsageAggregation.Period(entries, weekStart.ToString("yyyy-MM-dd"), weekStart, day);
            var month = UsageAggregation.Period(entries, UsageAggregation.MonthKey(day), monthStart, day);
            if (today is null && week.TotalTokens == 0 && month.TotalTokens == 0) return null;
            return new ProviderSnapshot(Id, DisplayName, today, UsageAggregation.ActiveBlock(entries, now),
                week, month, now, ReportsCost);
        }, cancellationToken);
}
