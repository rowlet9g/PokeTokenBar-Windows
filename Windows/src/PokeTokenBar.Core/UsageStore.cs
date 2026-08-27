namespace PokeTokenBar.Core;

public sealed class UsageStore : IDisposable
{
    private readonly IReadOnlyList<IUsageProvider> _providers;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateLock = new();
    private ProviderSnapshot[] _snapshots = [];
    private int _refreshPending;
    private bool _disposed;
    private bool _isRefreshing;
    private DateTimeOffset? _lastUpdated;
    private string? _lastErrorDescription;

    public UsageStore(IEnumerable<IUsageProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ProviderSnapshot> Snapshots
    {
        get
        {
            lock (_stateLock)
            {
                return _snapshots.ToArray();
            }
        }
    }

    public bool IsRefreshing
    {
        get
        {
            lock (_stateLock)
            {
                return _isRefreshing;
            }
        }
    }

    public DateTimeOffset? LastUpdated
    {
        get
        {
            lock (_stateLock)
            {
                return _lastUpdated;
            }
        }
    }

    public string? LastErrorDescription
    {
        get
        {
            lock (_stateLock)
            {
                return _lastErrorDescription;
            }
        }
    }

    public long TodayTotalTokens =>
        Snapshots.Aggregate(0L, (total, snapshot) =>
            UsageMath.SaturatingAdd(total, snapshot.TodayTotalTokens));

    public long WeekTotalTokens =>
        Snapshots.Aggregate(0L, (total, snapshot) =>
            UsageMath.SaturatingAdd(total, snapshot.WeekTotal?.TotalTokens ?? 0));

    public long MonthTotalTokens =>
        Snapshots.Aggregate(0L, (total, snapshot) =>
            UsageMath.SaturatingAdd(total, snapshot.MonthTotal?.TotalTokens ?? 0));

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Exchange(ref _refreshPending, 1);
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _refreshPending, 0);
                await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            while (Interlocked.Exchange(ref _refreshPending, 0) == 1
                   && !cancellationToken.IsCancellationRequested);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshGate.Dispose();
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            _isRefreshing = true;
        }

        OnChanged();
        var now = DateTimeOffset.Now;
        var tasks = _providers.Select(async provider =>
        {
            try
            {
                var snapshot = await provider.FetchAsync(now, cancellationToken).ConfigureAwait(false);
                return new ProviderOutcome(provider.Id, snapshot, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                return new ProviderOutcome(provider.Id, null, error.Message);
            }
        });

        try
        {
            var outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);
            lock (_stateLock)
            {
                var previous = _snapshots.ToDictionary(
                    snapshot => snapshot.ProviderId,
                    StringComparer.Ordinal);
                var next = new List<ProviderSnapshot>();
                var errors = new List<string>();
                foreach (var outcome in outcomes)
                {
                    if (outcome.ErrorDescription is not null)
                    {
                        errors.Add($"{outcome.ProviderId}: {outcome.ErrorDescription}");
                        if (previous.TryGetValue(outcome.ProviderId, out var preserved))
                        {
                            next.Add(preserved);
                        }

                        continue;
                    }

                    if (outcome.Snapshot is not null)
                    {
                        next.Add(outcome.Snapshot);
                    }
                }

                _snapshots = next.ToArray();
                _lastUpdated = now;
                _lastErrorDescription = errors.Count == 0 ? null : string.Join(" / ", errors);
            }
        }
        finally
        {
            lock (_stateLock)
            {
                _isRefreshing = false;
            }

            OnChanged();
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed record ProviderOutcome(
        string ProviderId,
        ProviderSnapshot? Snapshot,
        string? ErrorDescription);
}
