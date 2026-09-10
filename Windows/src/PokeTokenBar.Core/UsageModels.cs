namespace PokeTokenBar.Core;

public sealed record DailyUsage(
    string Date,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    long TotalTokens,
    double TotalCost);

public sealed record PeriodUsage(
    string Period,
    long TotalTokens,
    double TotalCost);

public sealed record BlockUsage(
    string Id,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    bool IsActive,
    long TotalTokens,
    double CostUsd,
    double? TokensPerMinute);

public sealed record ProviderSnapshot(
    string ProviderId,
    string DisplayName,
    DailyUsage? Today,
    BlockUsage? ActiveBlock,
    PeriodUsage? WeekTotal,
    PeriodUsage? MonthTotal,
    DateTimeOffset FetchedAt,
    bool ReportsCost = true,
    NativeUsage? NativeUsage = null)
{
    public long TodayTotalTokens => Today?.TotalTokens ?? 0;
}

public sealed record NativeUsage(
    string Unit,
    decimal Today,
    decimal Week,
    decimal Month,
    decimal? Balance = null,
    string? Plan = null,
    long? GrowthTokensPerUnit = null);

public sealed record UsageEntry(
    string Id,
    DateTimeOffset Timestamp,
    DateOnly LocalDay,
    string Model,
    long Input,
    long Output,
    long CacheWrite,
    long CacheRead,
    double? ExplicitCost = null)
{
    public long Total => UsageMath.SaturatingSum(Input, Output, CacheWrite, CacheRead);
}
