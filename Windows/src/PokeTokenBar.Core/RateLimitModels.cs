namespace PokeTokenBar.Core;

public sealed record RateLimitWindow(
    string Id,
    string DisplayName,
    double UsedPercent,
    DateTimeOffset? ResetsAt,
    int? WindowDurationMinutes)
{
    public double ClampedUsedPercent => Math.Clamp(UsedPercent, 0, 100);

    public double RemainingPercent => 100 - ClampedUsedPercent;
}

public sealed record ProviderRateLimitSnapshot(
    string ProviderId,
    string DisplayName,
    string? PlanType,
    IReadOnlyList<RateLimitWindow> Windows,
    DateTimeOffset FetchedAt)
{
    public double? MaxUsedPercent => Windows.Count == 0
        ? null
        : Windows.Max(window => window.ClampedUsedPercent);
}

public interface IRateLimitProvider
{
    string Id { get; }

    string DisplayName { get; }

    Task<ProviderRateLimitSnapshot?> FetchAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
