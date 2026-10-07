using System.Windows;
using PokeTokenBar.Core;

namespace PokeTokenBar.Windows;

public partial class MainWindow
{
    private bool _updateAvailable;
    public event Action<bool>? UpdateCheckRequested;
    public event EventHandler? UpdateInstallRequested;

    public void ShowUpdateState(AppUpdateRelease? release, string status, bool busy = false,
        double? progress = null, bool isError = false)
    {
        _updateAvailable = release is not null;
        UpdateVersionText.Text = $"현재 버전 v{ProductVersion}";
        UpdateButton.Content = release is not null ? $"v{release.Version} 업데이트" : "업데이트 확인";
        UpdateButton.IsEnabled = !busy;
        UpdateStatusText.Text = status;
        UpdateStatusText.Foreground = Brush(isError ? "#FFCC3333" : "#FF575B63");
        UpdateProgress.Visibility = progress is not null ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Value = Math.Clamp(progress ?? 0, 0, 1) * 100;
    }

    private void UpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_updateAvailable) UpdateInstallRequested?.Invoke(this, EventArgs.Empty);
        else UpdateCheckRequested?.Invoke(true);
    }

    private void SettingsView_OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (SettingsView.IsVisible) UpdateCheckRequested?.Invoke(false);
    }
}
