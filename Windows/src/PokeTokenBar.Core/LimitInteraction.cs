namespace PokeTokenBar.Core;

public sealed class LimitWindowProgress
{
    public bool Rewarded { get; set; }
    public int AlertTier { get; set; }
}

public sealed record LimitNotice(string Name, double UsedPercent, int Tier);
public sealed record CandyReward(string Name, int Count);
public sealed record LimitInteractionResult(IReadOnlyList<LimitNotice> Notices, IReadOnlyList<CandyReward> Rewards);

public static class LimitInteraction
{
    public const double WarningPercent = 80;
    public const double CriticalPercent = 95;

    public static bool IsCurrent(ProviderRateLimitSnapshot snapshot, RateLimitWindow window, DateTimeOffset now) =>
        double.IsFinite(window.UsedPercent) && snapshot.FetchedAt <= now
        && now - snapshot.FetchedAt <= TimeSpan.FromMinutes(2)
        && (window.ResetsAt is null || window.ResetsAt > now);

    public static bool CandyEligible(string provider, RateLimitWindow window) => provider switch
    {
        "claude" or "claude_code" => window.Id is "five_hour" or "seven_day",
        "codex" => window.Id.EndsWith(":primary", StringComparison.Ordinal)
            || window.Id.EndsWith(":secondary", StringComparison.Ordinal),
        "antigravity" => window.WindowDurationMinutes is 300 or 10080,
        _ => false,
    };

    public static string Mood(bool active, bool hasUsage, long today, double burnPerMinute,
        IEnumerable<ProviderRateLimitSnapshot> limits, DateTimeOffset now)
    {
        if (!active) return "새로운 만남을 준비하고 있어요.";
        if (limits.Any(snapshot => snapshot.Windows.Any(window =>
                IsCurrent(snapshot, window, now) && window.ClampedUsedPercent >= CriticalPercent)))
            return "한도가 가까워요. 잠시 쉬어 가도 좋아요.";
        if (!hasUsage || today == 0) return "다음 작업을 기다리며 잠들어 있어요.";
        if (burnPerMinute <= 1000) return "다음 작업을 기다리며 쉬고 있어요.";
        return burnPerMinute < 100000 ? "함께 작업하고 있어요." : "지금은 집중 모드예요.";
    }
}
