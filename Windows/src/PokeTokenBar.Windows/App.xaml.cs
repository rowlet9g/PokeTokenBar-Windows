using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Windows;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = "Local\\io.github.chattymin.PokeTokenBar.Windows";

    private SingleInstanceGuard? _singleInstance;
    private TrayIconController? _trayIcon;
    private MainWindow? _popover;
    private Icon? _appIcon;

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

        _appIcon = LoadAppIcon();
        _popover = new MainWindow(paths);
        MainWindow = _popover;

        _trayIcon = new TrayIconController(_appIcon);
        _trayIcon.ToggleRequested += (_, _) => TogglePopover();
        _trayIcon.ExitRequested += (_, _) => ExitApplication();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _appIcon?.Dispose();
        _singleInstance?.Dispose();
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
