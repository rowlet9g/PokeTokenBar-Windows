using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using PokeTokenBar.Core;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Windows;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = "Local\\io.github.chattymin.PokeTokenBar.Windows";

    private SingleInstanceGuard? _singleInstance;
    private TrayIconController? _trayIcon;
    private MainWindow? _popover;
    private Icon? _appIcon;
    private UsageStore? _usageStore;
    private CompanionStore? _companionStore;
    private PokemonSpriteStore? _spriteStore;
    private HttpClient? _httpClient;
    private DispatcherTimer? _usageTimer;
    private readonly CancellationTokenSource _refreshCancellation = new();
    private int _spriteGeneration;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceGuard(SingleInstanceName);
        if (!_singleInstance.IsPrimaryInstance)
        {
            Shutdown();
            return;
        }

        var paths = WindowsAppPaths.CreateDefault();
        paths.EnsureDirectories();

        _usageStore = new UsageStore(
        [
            new CodexUsageProvider(WindowsCodexPaths.CreateDefaultRoots()),
        ]);
        _usageStore.Changed += UsageStore_OnChanged;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PokeTokenBar-Windows/0.1");
        var pokemonProvider = new PokeApiClient(
            _httpClient,
            Path.Combine(paths.CacheDirectory, "PokeAPI"));
        _spriteStore = new PokemonSpriteStore(
            _httpClient,
            Path.Combine(paths.CacheDirectory, "Sprites"));
        _companionStore = new CompanionStore(
            Path.Combine(paths.DataDirectory, "companion-state.json"),
            pokemonProvider);
        LogCompanionStateLoad(paths.LogsDirectory, _companionStore);
        if (_companionStore.LastPersistenceError is { } persistenceError)
        {
            LogCompanionPersistenceError(paths.LogsDirectory, persistenceError);
        }
        _companionStore.Changed += CompanionStore_OnChanged;

        _appIcon = LoadAppIcon();
        _popover = new MainWindow(
            paths,
            _usageStore,
            _companionStore,
            _spriteStore,
            _refreshCancellation.Token);
        _popover.RefreshRequested += Popover_OnRefreshRequested;
        MainWindow = _popover;

        _trayIcon = new TrayIconController(_appIcon);
        _trayIcon.ToggleRequested += (_, _) => TogglePopover();
        _trayIcon.ExitRequested += (_, _) => ExitApplication();

        // A newly registered notification icon can be placed in Windows' overflow
        // area. Showing the popup once makes first launch discoverable; after it is
        // dismissed the application continues to behave as a tray-only app.
        _popover.ShowNearNotificationArea(hideOnDeactivate: false);

        _usageTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(2),
        };
        _usageTimer.Tick += UsageTimer_OnTick;
        _usageTimer.Start();

        ApplyUsageState();
        Dispatcher.BeginInvoke(new Action(async () => await RefreshUsageAsync()));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _usageTimer?.Stop();
        if (_usageTimer is not null)
        {
            _usageTimer.Tick -= UsageTimer_OnTick;
        }

        _refreshCancellation.Cancel();
        if (_popover is not null)
        {
            _popover.RefreshRequested -= Popover_OnRefreshRequested;
        }

        if (_usageStore is not null)
        {
            _usageStore.Changed -= UsageStore_OnChanged;
        }

        if (_companionStore is not null)
        {
            _companionStore.Changed -= CompanionStore_OnChanged;
        }

        _trayIcon?.Dispose();
        _usageStore?.Dispose();
        _httpClient?.Dispose();
        _appIcon?.Dispose();
        _singleInstance?.Dispose();
        _refreshCancellation.Dispose();
        base.OnExit(e);
    }

    private void TogglePopover()
    {
        if (_popover is null)
        {
            return;
        }

        if (_popover.IsVisible)
        {
            _popover.Hide();
        }
        else
        {
            _popover.ShowNearNotificationArea();
        }
    }

    private void ExitApplication()
    {
        _popover?.Close();
        Shutdown();
    }

    private async void Popover_OnRefreshRequested(object? sender, EventArgs e)
    {
        await RefreshUsageAsync();
    }

    private async void UsageTimer_OnTick(object? sender, EventArgs e)
    {
        await RefreshUsageAsync();
    }

    private async Task RefreshUsageAsync()
    {
        if (_usageStore is null || _refreshCancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await _usageStore.RefreshAsync(_refreshCancellation.Token);
            ApplyUsageState();
            if (_companionStore is not null)
            {
                await _companionStore.EnsureHatchedAsync(_refreshCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_refreshCancellation.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private void UsageStore_OnChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyUsageState();
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(ApplyUsageState));
        }
    }

    private void CompanionStore_OnChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyCompanionState();
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(ApplyCompanionState));
        }
    }

    private void ApplyUsageState()
    {
        if (_usageStore is null)
        {
            return;
        }

        var todayByProvider = _usageStore.TodayTokensByProvider;
        _companionStore?.Update(
            todayByProvider,
            DateOnly.FromDateTime(DateTime.Now),
            hasUsageData: _usageStore.Snapshots.Count > 0);
        _popover?.ApplyUsageState();
        ApplyCompanionState();
    }

    private void ApplyCompanionState()
    {
        if (_usageStore is null || _companionStore is null)
        {
            return;
        }

        _popover?.ApplyCompanionState();
        QueueSpriteRefresh();
        var compact = TokenFormatter.Compact(_usageStore.TodayTotalTokens);
        if (_companionStore.HasActivePokemon)
        {
            var name = _companionStore.CurrentPokemonName ?? "Pokémon";
            var growthPercent = (int)Math.Round(_companionStore.GrowthProgress * 100);
            _trayIcon?.UpdateTooltip($"PokeTokenBar · {compact} today · {name} {growthPercent}%");
        }
        else
        {
            var eggPercent = (int)Math.Round(_companionStore.EggProgress * 100);
            _trayIcon?.UpdateTooltip($"PokeTokenBar · {compact} today · Egg {eggPercent}%");
        }
    }

    private void QueueSpriteRefresh()
    {
        if (_popover is null || _companionStore is null || _spriteStore is null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _spriteGeneration);
        if (_companionStore.CurrentSpeciesId is not { } speciesId)
        {
            _popover.SetPokemonSprite(null);
            return;
        }

        var shiny = _companionStore.IsCurrentPokemonShiny;
        _ = LoadPokemonSpriteAsync(speciesId, shiny, generation);
    }

    private async Task LoadPokemonSpriteAsync(int speciesId, bool shiny, int generation)
    {
        if (_spriteStore is null)
        {
            return;
        }

        try
        {
            var bytes = await _spriteStore.GetSpriteAsync(
                speciesId,
                shiny,
                _refreshCancellation.Token);
            if (generation != Volatile.Read(ref _spriteGeneration)
                || _refreshCancellation.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() => _popover?.SetPokemonSprite(bytes));
        }
        catch (OperationCanceledException) when (_refreshCancellation.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch
        {
            // The name and growth state remain useful while the sprite host is offline.
        }
    }

    private static Icon LoadAppIcon()
    {
        var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/icon.png"))
            ?? throw new InvalidOperationException("The tray icon resource is missing.");

        using var stream = resource.Stream;
        using var bitmap = new Bitmap(stream);
        var handle = bitmap.GetHicon();

        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void LogCompanionPersistenceError(string logsDirectory, string description)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:O} {description}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(logsDirectory, "companion-errors.log"), line);
        }
        catch (IOException)
        {
            // The visible popup still reports the persistence failure.
        }
        catch (UnauthorizedAccessException)
        {
            // The visible popup still reports the persistence failure.
        }
    }

    private static void LogCompanionStateLoad(string logsDirectory, CompanionStore store)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:O} {store.StateFilePath} | "
                + $"{store.StateLoadDescription}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(logsDirectory, "companion-load.log"), line);
        }
        catch (IOException)
        {
            // State loading is unaffected when diagnostics cannot be written.
        }
        catch (UnauthorizedAccessException)
        {
            // State loading is unaffected when diagnostics cannot be written.
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
