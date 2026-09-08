using System.Windows;
using System.Windows.Controls;
using PokeTokenBar.Core;

namespace PokeTokenBar.Windows;

public partial class MainWindow
{
    private TokenPeriod _tokenPeriod;

    private void TokensTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyTokensState();
        ShowTab(MainTab.Tokens);
    }

    private void TokensPeriodButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string tag }
            && Enum.TryParse<TokenPeriod>(tag, out var period))
        {
            _tokenPeriod = period;
            ApplyTokensState();
        }
    }

    private void ApplyTokensState()
    {
        var rows = _usageStore.Snapshots.Select(snapshot => (Snapshot: snapshot, Total: _tokenPeriod switch
        {
            TokenPeriod.Week => snapshot.WeekTotal?.TotalTokens ?? 0,
            TokenPeriod.Month => snapshot.MonthTotal?.TotalTokens ?? 0,
            _ => snapshot.TodayTotalTokens,
        })).OrderByDescending(row => row.Total).ThenBy(row => row.Snapshot.DisplayName).ToArray();
        var total = rows.Aggregate(0L, (sum, row) => UsageMath.SaturatingAdd(sum, row.Total));
        TokensPeriodText.Text = _tokenPeriod switch
        {
            TokenPeriod.Week => "이번 주 전체 토큰",
            TokenPeriod.Month => "이번 달 전체 토큰",
            _ => "오늘 전체 토큰",
        };
        TokensTotalText.Text = TokenFormatter.Grouped(total);
        SetTabStyle(TokensTodayButton, _tokenPeriod == TokenPeriod.Today);
        SetTabStyle(TokensWeekButton, _tokenPeriod == TokenPeriod.Week);
        SetTabStyle(TokensMonthButton, _tokenPeriod == TokenPeriod.Month);
        TokensEmptyText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        TokensProviderItems.ItemsSource = rows.Where(row => row.Total > 0).Select(row =>
        {
            var snapshot = row.Snapshot;
            var today = _tokenPeriod == TokenPeriod.Today ? snapshot.Today : null;
            var share = total > 0 ? (double)row.Total / total * 100 : 0;
            var detail = today is null ? string.Empty
                : $"Input {TokenFormatter.Grouped(today.InputTokens)} · Output {TokenFormatter.Grouped(today.OutputTokens)}"
                  + $"\nCache {TokenFormatter.Grouped(UsageMath.SaturatingAdd(today.CacheCreationTokens, today.CacheReadTokens))}"
                  + $" (쓰기 {TokenFormatter.Grouped(today.CacheCreationTokens)} / 읽기 {TokenFormatter.Grouped(today.CacheReadTokens)})";
            if (snapshot.ProviderId == "kiro") detail += "\n텍스트 바이트 기반 추정치 · 실제 사용량과 다를 수 있음";
            return new TokenProviderCard(
                snapshot.ProviderId == "gemini" ? "Gemini CLI (Legacy)" : snapshot.DisplayName,
                TokenFormatter.Grouped(row.Total), share, $"{share:0.0}%", detail,
                today is null && snapshot.ProviderId != "kiro" ? Visibility.Collapsed : Visibility.Visible,
                $"{snapshot.FetchedAt.LocalDateTime:MM/dd HH:mm:ss} 갱신");
        }).ToArray();

        TokensStatusText.Foreground = Brush(_usageStore.LastErrorDescription is null ? "#FF7D8797" : "#FFFF806B");
        TokensStatusText.Text = _usageStore.IsRefreshing
            ? "로컬 AI 로그를 읽는 중 · 이전 수치를 표시합니다"
            : _usageStore.LastErrorDescription is { } error
                ? $"일부 공급자 갱신 실패 · 이전 기록이 포함될 수 있습니다 · {error}"
                : _usageStore.LastUpdated is { } updated
                    ? $"{updated.LocalDateTime:HH:mm:ss} 갱신 · 사용량이 0인 공급자는 숨김"
                    : "로컬 사용량 연결 준비 중";
    }

    private enum TokenPeriod { Today, Week, Month }

    private sealed record TokenProviderCard(
        string Name, string TotalText, double Share, string ShareText,
        string Detail, Visibility DetailVisibility, string UpdatedText);
}
