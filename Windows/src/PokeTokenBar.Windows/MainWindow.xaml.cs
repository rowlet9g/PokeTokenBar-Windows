using System.Windows;
using PokeTokenBar.Core;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Windows;

public partial class MainWindow : Window
{
    private readonly UsageStore _usageStore;

    public MainWindow(WindowsAppPaths paths, UsageStore usageStore)
    {
        _usageStore = usageStore;
        InitializeComponent();
        StoragePathText.Text = paths.DataDirectory;
        ApplyUsageState();
    }

    public event EventHandler? RefreshRequested;

    public void ApplyUsageState()
    {
        var today = _usageStore.TodayTotalTokens;
        TokenValueText.Text = TokenFormatter.Compact(today);
        ExactTokenValueText.Text = TokenFormatter.Grouped(today);
        WeekValueText.Text = TokenFormatter.Compact(_usageStore.WeekTotalTokens);
        MonthValueText.Text = TokenFormatter.Compact(_usageStore.MonthTotalTokens);
        RefreshButton.IsEnabled = !_usageStore.IsRefreshing;

        if (_usageStore.IsRefreshing)
        {
            StatusText.Text = "Codex 로그를 읽는 중...";
            return;
        }

        if (_usageStore.LastErrorDescription is { } error)
        {
            StatusText.Text = $"새로고침 실패 · {error}";
            return;
        }

        if (_usageStore.Snapshots.Count == 0)
        {
            StatusText.Text = _usageStore.LastUpdated is null
                ? "Codex 로그 연결 준비 중"
                : "오늘 기록된 Codex 사용량이 없습니다";
            return;
        }

        StatusText.Text = _usageStore.LastUpdated is { } updated
            ? $"Codex · {updated.LocalDateTime:HH:mm:ss} 갱신"
            : "Codex";
    }

    public void ShowNearNotificationArea()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - Height - 12;
        Show();
        Activate();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        Hide();
    }

    private void HideButton_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }
}
