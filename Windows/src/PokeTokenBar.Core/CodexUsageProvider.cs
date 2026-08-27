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
        var localDay = DateOnly.FromDateTime(now.LocalDateTime);
        var firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var weekStart = UsageAggregation.StartOfWeek(localDay, firstDayOfWeek);
        var monthStart = new DateOnly(localDay.Year, localDay.Month, 1);
        var daily = UsageAggregation.Daily(entries, localDay);
        var block = UsageAggregation.ActiveBlock(entries, now);

        if (daily is null && block is null)
        {
            return null;
        }

        return new ProviderSnapshot(
            Id,
            DisplayName,
            daily,
            block,
            UsageAggregation.Period(
                entries,
                weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                weekStart,
                localDay),
            UsageAggregation.Period(
                entries,
                UsageAggregation.MonthKey(localDay),
                monthStart,
                localDay),
            now,
            ReportsCost);
    }
}
