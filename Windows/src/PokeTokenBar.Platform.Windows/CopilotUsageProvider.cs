using System.Globalization;
using PokeTokenBar.Core;

namespace PokeTokenBar.Platform.Windows;

public sealed class CopilotUsageProvider : IUsageProvider
{
    private readonly CopilotUsageReader _reader;
    private readonly IReadOnlyList<string> _roots;

    public CopilotUsageProvider(
        IEnumerable<string> roots,
        CopilotUsageReader? reader = null)
    {
        _roots = roots.Select(Path.GetFullPath).ToArray();
        _reader = reader ?? new CopilotUsageReader();
    }

    public string Id => "copilot";

    public string DisplayName => "Copilot";

    public bool ReportsCost => false;

    public Task<ProviderSnapshot?> FetchAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Fetch(now, cancellationToken), cancellationToken);

    private ProviderSnapshot? Fetch(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entries = _reader.ReadEntries(
            _roots,
            UsageAggregation.EnrichmentScanStart(now),
            cancellationToken);
        var localDay = DateOnly.FromDateTime(now.LocalDateTime);
        var firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var weekStart = UsageAggregation.StartOfWeek(localDay, firstDayOfWeek);
        var monthStart = new DateOnly(localDay.Year, localDay.Month, 1);
        var daily = UsageAggregation.Daily(entries, localDay);
        var block = UsageAggregation.ActiveBlock(entries, now);
        var week = UsageAggregation.Period(
            entries,
            weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            weekStart,
            localDay);
        var month = UsageAggregation.Period(
            entries,
            UsageAggregation.MonthKey(localDay),
            monthStart,
            localDay);
        if (daily is null
            && block is null
            && week.TotalTokens == 0
            && month.TotalTokens == 0)
        {
            return null;
        }

        return new ProviderSnapshot(
            Id,
            DisplayName,
            daily,
            block,
            week,
            month,
            now,
            ReportsCost);
    }
}
