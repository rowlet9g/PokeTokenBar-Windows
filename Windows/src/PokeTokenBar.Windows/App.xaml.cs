using System.Drawing;
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
    private DispatcherTimer? _usageTimer;
    private readonly CancellationTokenSource _refreshCancellation = new();

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

        _appIcon = LoadAppIcon();
        _popover = new MainWindow(paths, _usageStore);
        _popover.RefreshRequested += Popover_OnRefreshRequested;
        MainWindow = _popover;

        _trayIcon = new TrayIconController(_appIcon);
        _trayIcon.ToggleRequested += (_, _) => TogglePopover();
        _trayIcon.ExitRequested += (_, _) => ExitApplication();

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

        _trayIcon?.Dispose();
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

    private void ApplyUsageState()
    {
        if (_usageStore is null)
        {
            return;
        }

        _popover?.ApplyUsageState();
        var compact = TokenFormatter.Compact(_usageStore.TodayTotalTokens);
        _trayIcon?.UpdateTooltip($"PokeTokenBar · Codex today {compact}");
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
