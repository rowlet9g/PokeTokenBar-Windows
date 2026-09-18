using System.Globalization;

namespace PokeTokenBar.Core;

public sealed class CodexUsageProvider : IUsageProvider
{
    private readonly CodexUsageReader _reader;
    private readonly IReadOnlyList<string> _roots;

    public CodexUsageProvider(
        IEnumerable<string> roots,
        CodexUsageReader? reader = null)
    {
        _roots = roots.Select(Path.GetFullPath).ToArray();
        _reader = reader ?? new CodexUsageReader();
    }

    public string Id => "codex";

    public string DisplayName => "Codex";

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
        return BuildSnapshot(Id, DisplayName, entries, now, ReportsCost);
    }

    public static ProviderSnapshot? BuildSnapshot(
        string providerId,
        string displayName,
        IEnumerable<UsageEntry> entries,
        DateTimeOffset now,
        bool reportsCost = false)
    {
        var materialized = entries as IReadOnlyList<UsageEntry> ?? entries.ToArray();
        var localDay = DateOnly.FromDateTime(now.LocalDateTime);
        var firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var weekStart = UsageAggregation.StartOfWeek(localDay, firstDayOfWeek);
        var monthStart = new DateOnly(localDay.Year, localDay.Month, 1);
        var daily = UsageAggregation.Daily(materialized, localDay);
        var block = UsageAggregation.ActiveBlock(materialized, now);
        var week = UsageAggregation.Period(
            materialized,
            weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            weekStart,
            localDay);
        var month = UsageAggregation.Period(
            materialized,
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
            providerId,
            displayName,
            daily,
            block,
            week,
            month,
            now,
            reportsCost);
    }
}
