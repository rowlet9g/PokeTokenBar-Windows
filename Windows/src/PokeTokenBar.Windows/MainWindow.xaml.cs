using System.Windows;
using PokeTokenBar.Core;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Windows;

public partial class MainWindow : Window
{
    private readonly UsageStore _usageStore;
    private readonly CompanionStore _companionStore;

    public MainWindow(
        WindowsAppPaths paths,
        UsageStore usageStore,
        CompanionStore companionStore)
    {
        _usageStore = usageStore;
        _companionStore = companionStore;
        InitializeComponent();
        StoragePathText.Text = paths.DataDirectory;
        ApplyUsageState();
        ApplyCompanionState();
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

    public void ApplyCompanionState()
    {
        var progress = _companionStore.EggProgress;
        var percent = (int)Math.Round(progress * 100);
        EggProgressBar.Value = progress;
        CompanionTitleText.Text = $"새 알 · {percent}%";

        if (!_companionStore.InstallBaselineSet)
        {
            CompanionProgressText.Text = "첫 사용량 동기화 후 부화를 시작합니다";
            return;
        }

        if (_companionStore.ReadyToHatch)
        {
            CompanionTitleText.Text = "새 알 · 부화 준비 완료";
            CompanionProgressText.Text = "다음 단계에서 포켓몬을 만나게 됩니다";
            return;
        }

        CompanionProgressText.Text = _companionStore.EggStarted
            ? $"{TokenFormatter.Compact(_companionStore.EggTokensToHatch)} 토큰 후 부화"
            : "다음 사용량부터 알이 자라기 시작합니다";
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
