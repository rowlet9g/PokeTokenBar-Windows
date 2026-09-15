namespace PokeTokenBar.Core;

public sealed partial class CompanionStore
{
    public LimitInteractionResult ApplyOfficialLimits(
        IEnumerable<ProviderRateLimitSnapshot> freshSnapshots, DateTimeOffset now)
    {
        var notices = new List<LimitNotice>();
        var rewards = new List<CandyReward>();
        var changed = false;
        lock (_stateLock)
        {
            if (!_persistenceEnabled) return new(notices, rewards);
            var previous = SaveTransfer.CloneState(_state);
            foreach (var snapshot in freshSnapshots)
            foreach (var window in snapshot.Windows)
            {
                if (!LimitInteraction.IsCurrent(snapshot, window, now)) continue;
                // Stable identity: changing reset timestamps must not issue repeated rewards.
                var key = snapshot.ProviderId + "|" + window.Id;
                var used = window.ClampedUsedPercent;
                var tier = used >= LimitInteraction.CriticalPercent ? 2
                    : used >= LimitInteraction.WarningPercent ? 1 : 0;
                var eligible = LimitInteraction.CandyEligible(snapshot.ProviderId, window);
                if (!_state.LimitProgress.TryGetValue(key, out var progress))
                {
                    // Seed each newly observed window, including providers that load late.
                    progress = new LimitWindowProgress { Rewarded = eligible && used >= 100 };
                    _state.LimitProgress[key] = progress;
                    changed = true;
                }
                else if (eligible)
                {
                    if (used < 100 && progress.Rewarded)
                    {
                        progress.Rewarded = false;
                        changed = true;
                    }
                    else if (used >= 100 && !progress.Rewarded)
                    {
                        progress.Rewarded = true;
                        var count = window.WindowDurationMinutes > 1440 ? 5 : 1;
                        var itemKey = CompanionItemRules.StorageKey(CompanionItemKind.RareCandy);
                        _state.Inventory[itemKey] = (int)Math.Min(1_000_000L,
                            (long)ItemCountUnsafe(CompanionItemKind.RareCandy) + count);
                        rewards.Add(new CandyReward($"{snapshot.DisplayName} · {window.DisplayName}", count));
                        changed = true;
                    }
                }
                if (tier == 0 && progress.AlertTier != 0)
                {
                    progress.AlertTier = 0;
                    changed = true;
                }
                else if (tier > progress.AlertTier)
                {
                    progress.AlertTier = tier;
                    notices.Add(new LimitNotice($"{snapshot.DisplayName} · {window.DisplayName}", used, tier));
                    changed = true;
                }
            }
            if (changed)
            {
                TrySaveState();
                if (_lastPersistenceError is { } error)
                {
                    _state = previous;
                    throw new IOException("한도 보상을 저장하지 못했습니다. 다음 갱신에서 재시도합니다. " + error);
                }
            }
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return new(notices, rewards);
    }
}
