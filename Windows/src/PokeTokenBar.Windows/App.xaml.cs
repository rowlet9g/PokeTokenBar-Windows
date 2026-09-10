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
    private FloatingPetWindow? _floatingPet;
    private Icon? _appIcon;
    private UsageStore? _usageStore;
    private RateLimitStore? _rateLimitStore;
    private CompanionStore? _companionStore;
    private CompanionMilestoneTracker? _milestoneTracker;
    private AppSettingsStore? _settingsStore;
    private AppSettings _settings = new();
    private WindowsStartupRegistration? _startupRegistration;
    private string? _logsDirectory;
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
            _singleInstance.SignalPrimary();
            Shutdown();
            return;
        }

        var paths = WindowsAppPaths.CreateDefault();
        try
        {
            paths.EnsureDirectories();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            System.Windows.MessageBox.Show("저장소를 준비하지 못했습니다. 기존 진행은 변경하지 않았습니다.\n\n" + error.Message,
                "PokeTokenBar 저장소 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        _logsDirectory = paths.LogsDirectory;
        _settingsStore = new AppSettingsStore(Path.Combine(paths.DataDirectory, "settings.json"));
        _settings = _settingsStore.Current;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installedExecutable = Path.Combine(
            localAppData,
            "Programs",
            "PokeTokenBar",
            "PokeTokenBar.Windows.exe");
        _startupRegistration = new WindowsStartupRegistration(installedExecutable);
        if (_settings.LaunchAtLogin)
        {
            TryRepairStartupRegistration();
        }

        var providers = new List<IUsageProvider>
        {
            new CodexUsageProvider(WindowsCodexPaths.CreateDefaultRoots()),
            new GeminiUsageProvider(WindowsGeminiPaths.CreateDefaultRoots()),
            new AntigravityUsageProvider(WindowsAntigravityPaths.CreateDefaultRoots()),
            new CursorUsageProvider(WindowsCursorPaths.CreateDefaultRoots()),
            new CopilotUsageProvider(WindowsCopilotPaths.CreateDefaultRoots()),
        };
        foreach (var providerId in WindowsLocalToolPaths.ProviderIds)
        {
            var reader = new LocalToolUsageReader(providerId);
            var displayName = providerId switch
            {
                "claude_code" => "Claude Code", "opencode" => "OpenCode", "hermes" => "Hermes Agent",
                "grok" => "Grok CLI", "kiro" => "Kiro CLI (추정)", "pi" => "Pi Agent", _ => "omp",
            };
            providers.Add(new LocalToolUsageProvider(providerId, displayName,
                (since, token) => reader.ReadEntries(WindowsLocalToolPaths.CreateRoots(providerId), since, token)));
        }
        _usageStore = new UsageStore(providers);
        _usageStore.Changed += UsageStore_OnChanged;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PokeTokenBar-Windows/0.1");
        _rateLimitStore = new RateLimitStore(
            [
                new CodexRateLimitsProvider(),
                new ClaudeRateLimitsProvider(_httpClient),
                new AntigravityRateLimitsProvider(_httpClient),
            ]);
        _rateLimitStore.Changed += RateLimitStore_OnChanged;
        var pokemonProvider = new PokeApiClient(
            _httpClient,
            Path.Combine(paths.CacheDirectory, "PokeAPI"));
        _spriteStore = new PokemonSpriteStore(
            _httpClient,
            Path.Combine(paths.CacheDirectory, "Sprites"));
        _companionStore = new CompanionStore(
            Path.Combine(paths.DataDirectory, "companion-state.json"),
            pokemonProvider);
        _milestoneTracker = new CompanionMilestoneTracker(CaptureCompanionSnapshot());
        LogCompanionStateLoad(paths.LogsDirectory, _companionStore);
        if (_companionStore.LastPersistenceError is { } persistenceError)
        {
            LogCompanionPersistenceError(paths.LogsDirectory, persistenceError);
        }
        _companionStore.Changed += CompanionStore_OnChanged;

        _appIcon = LoadAppIcon();
        _popover = new MainWindow(
            _usageStore,
            _rateLimitStore,
            _companionStore,
            _spriteStore,
            _settings,
            _refreshCancellation.Token);
        _popover.RefreshRequested += Popover_OnRefreshRequested;
        _popover.SettingsChanged += Popover_OnSettingsChanged;
        MainWindow = _popover;

        _floatingPet = new FloatingPetWindow();
        _floatingPet.OpenRequested += FloatingPet_OnOpenRequested;
        _floatingPet.HideRequested += FloatingPet_OnHideRequested;
        _floatingPet.PositionChanged += FloatingPet_OnPositionChanged;
        _floatingPet.ApplySettings(_settings);

        _trayIcon = new TrayIconController(_appIcon);
        _trayIcon.ToggleRequested += (_, _) => TogglePopover();
        _trayIcon.ExitRequested += (_, _) => ExitApplication();
        _trayIcon.NotificationClicked += (_, _) => ShowPopover();
        _singleInstance.StartListening(() =>
            Dispatcher.BeginInvoke(new Action(ShowPopover)));

        // A newly registered notification icon can be placed in Windows' overflow
        // area. Showing the popup once makes first launch discoverable; after it is
        // dismissed the application continues to behave as a tray-only app.
        var backgroundLaunch = e.Args.Any(argument =>
            string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        if (!backgroundLaunch)
        {
            _popover.ShowNearNotificationArea(hideOnDeactivate: false);
        }

        _usageTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes),
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
        _companionStore?.Persist();
        if (_popover is not null)
        {
            _popover.RefreshRequested -= Popover_OnRefreshRequested;
            _popover.SettingsChanged -= Popover_OnSettingsChanged;
        }

        if (_floatingPet is not null)
        {
            _floatingPet.OpenRequested -= FloatingPet_OnOpenRequested;
            _floatingPet.HideRequested -= FloatingPet_OnHideRequested;
            _floatingPet.PositionChanged -= FloatingPet_OnPositionChanged;
            _floatingPet.Close();
        }

        if (_usageStore is not null)
        {
            _usageStore.Changed -= UsageStore_OnChanged;
        }

        if (_rateLimitStore is not null)
        {
            _rateLimitStore.Changed -= RateLimitStore_OnChanged;
        }

        if (_companionStore is not null)
        {
            _companionStore.Changed -= CompanionStore_OnChanged;
        }

        _trayIcon?.Dispose();
        _usageStore?.Dispose();
        _rateLimitStore?.Dispose();
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

    private void ShowPopover()
    {
        if (_popover is null)
        {
            return;
        }

        _popover.ShowNearNotificationArea();
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

    private void Popover_OnSettingsChanged(AppSettings requested)
    {
        if (_settingsStore is null || _startupRegistration is null || _popover is null)
        {
            return;
        }

        var previous = _settings;
        try
        {
            _startupRegistration.SetEnabled(requested.LaunchAtLogin);
            _settingsStore.Save(requested);
            _settings = _settingsStore.Current;
            ApplyRuntimeSettings();
            _popover.ApplySettings(_settings);
            _popover.ShowSettingsStatus("설정을 저장했습니다");
        }
        catch (Exception error) when (error is IOException
                                           or UnauthorizedAccessException
                                           or System.Security.SecurityException
                                           or InvalidOperationException)
        {
            try
            {
                _startupRegistration.SetEnabled(previous.LaunchAtLogin);
            }
            catch
            {
                // Preserve the original settings error shown below.
            }

            _settings = previous;
            _popover.ApplySettings(previous);
            _popover.ShowSettingsStatus($"설정 저장 실패 · {error.Message}", isError: true);
            LogSettingsError(error.Message);
        }
    }

    private void ApplyRuntimeSettings()
    {
        if (_usageTimer is not null)
        {
            _usageTimer.Interval = TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes);
        }

        _popover?.ApplySettings(_settings);
        _floatingPet?.ApplySettings(_settings);
    }

    private void TryRepairStartupRegistration()
    {
        try
        {
            _startupRegistration?.SetEnabled(enabled: true);
        }
        catch (Exception error) when (error is IOException
                                           or UnauthorizedAccessException
                                           or System.Security.SecurityException
                                           or InvalidOperationException)
        {
            LogSettingsError(error.Message);
        }
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
            var usageRefresh = _usageStore.RefreshAsync(_refreshCancellation.Token);
            var limitRefresh = _rateLimitStore?.RefreshAsync(_refreshCancellation.Token)
                ?? Task.CompletedTask;
            await Task.WhenAll(usageRefresh, limitRefresh);
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

    private void RateLimitStore_OnChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            _popover?.ApplyRateLimitState();
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => _popover?.ApplyRateLimitState()));
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
        ApplyFloatingCompanionState();
        QueueSpriteRefresh();
        ShowCompanionMilestoneIfNeeded();
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

    private void ApplyFloatingCompanionState()
    {
        if (_usageStore is null || _companionStore is null || _floatingPet is null)
        {
            return;
        }

        var today = TokenFormatter.Grouped(_usageStore.TodayTotalTokens);
        var tooltip = _companionStore.HasActivePokemon
            ? $"{_companionStore.CurrentPokemonName ?? "Pokémon"} · 오늘 {today} 토큰"
            : $"새 알 · 오늘 {today} 토큰";
        _floatingPet.UpdateCompanionState(_companionStore.HasActivePokemon, tooltip);
    }

    private CompanionMilestoneSnapshot CaptureCompanionSnapshot()
    {
        if (_companionStore is null)
        {
            return default;
        }

        return new CompanionMilestoneSnapshot(
            _companionStore.CurrentSpeciesId,
            _companionStore.CurrentPokemonName,
            _companionStore.HasActivePokemon ? _companionStore.CurrentStage : 0,
            _companionStore.IsCurrentPokemonShiny,
            _companionStore.DexCount);
    }

    private void ShowCompanionMilestoneIfNeeded()
    {
        var milestone = _milestoneTracker?.Observe(CaptureCompanionSnapshot());
        if (milestone is null)
        {
            return;
        }

        _popover?.QueueMilestoneAnimation(milestone);
        if (_trayIcon is null || !_settings.NotificationsEnabled)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(milestone.PokemonName)
            ? "포켓몬"
            : milestone.PokemonName;
        var shiny = milestone.IsShiny ? "이로치 " : string.Empty;
        var (title, message) = milestone.Kind switch
        {
            CompanionMilestoneKind.Hatched =>
                ("포켓몬이 태어났어요!", $"새로운 동료: {shiny}{name}"),
            CompanionMilestoneKind.Evolved =>
                ("포켓몬이 진화했어요!", $"새로운 모습: {shiny}{name}"),
            CompanionMilestoneKind.Graduated =>
                ("육성을 완료했어요!", $"{shiny}{name}의 기록이 도감에 등록되었습니다."),
            _ => throw new ArgumentOutOfRangeException(),
        };
        _trayIcon.ShowNotification(title, message);
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
            _floatingPet?.SetPokemonSprite(null);
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

            await Dispatcher.InvokeAsync(() =>
            {
                _popover?.SetPokemonSprite(bytes);
                _floatingPet?.SetPokemonSprite(bytes);
            });
        }
        catch (OperationCanceledException) when (_refreshCancellation.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception error) when (error is HttpRequestException
                                           or IOException
                                           or InvalidDataException
                                           or UnauthorizedAccessException)
        {
            // The name and growth state remain useful while the sprite host is offline.
            LogSpriteError(speciesId, shiny, error.Message);
        }
    }

    private void FloatingPet_OnOpenRequested(object? sender, EventArgs e) => TogglePopover();

    private void FloatingPet_OnHideRequested(object? sender, EventArgs e)
    {
        SaveFloatingPetSettings(_settings with { FloatingPetEnabled = false });
    }

    private void FloatingPet_OnPositionChanged(double left, double top)
    {
        SaveFloatingPetSettings(_settings with
        {
            FloatingPetLeft = left,
            FloatingPetTop = top,
        });
    }

    private void SaveFloatingPetSettings(AppSettings requested)
    {
        if (_settingsStore is null)
        {
            return;
        }

        try
        {
            _settingsStore.Save(requested);
            _settings = _settingsStore.Current;
            _popover?.ApplySettings(_settings);
            _floatingPet?.ApplySettings(_settings);
            if (_settings.FloatingPetEnabled)
            {
                ApplyFloatingCompanionState();
                QueueSpriteRefresh();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LogSettingsError(error.Message);
        }
    }

    private void LogSpriteError(int speciesId, bool shiny, string description)
    {
        if (_logsDirectory is null)
        {
            return;
        }

        try
        {
            var variant = shiny ? "shiny" : "normal";
            var line = $"{DateTimeOffset.Now:O} species #{speciesId} ({variant}) | "
                + $"{description}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(_logsDirectory, "sprite-errors.log"), line);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Sprite loading remains unaffected when diagnostics cannot be written.
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
            // Companion operation is unaffected when diagnostics cannot be written.
        }
        catch (UnauthorizedAccessException)
        {
            // Companion operation is unaffected when diagnostics cannot be written.
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

    private void LogSettingsError(string description)
    {
        if (_logsDirectory is null)
        {
            return;
        }

        try
        {
            var line = $"{DateTimeOffset.Now:O} {description}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(_logsDirectory, "settings-errors.log"), line);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Settings remain usable even when diagnostics cannot be written.
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
