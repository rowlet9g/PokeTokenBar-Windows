using System.IO;
using System.Net.Http;
using PokeTokenBar.Core;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Windows;

public partial class App
{
    private HttpClient? _updateHttp;
    private AppUpdateClient? _updates;
    private AppUpdateRelease? _availableUpdate;
    private bool _updateBusy;
    private DateTimeOffset _lastUpdateCheck;
    private WindowsAppPaths? _updatePaths;

    private void InitializeUpdates(WindowsAppPaths paths)
    {
        _updatePaths = paths;
        _updateHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _updates = new AppUpdateClient(_updateHttp);
        _popover!.ShowUpdateState(null, "설정을 열면 새 버전을 확인합니다.");
        _popover.UpdateCheckRequested += Popover_OnUpdateCheck;
        _popover.UpdateInstallRequested += Popover_OnUpdateInstall;
    }

    private async void Popover_OnUpdateCheck(bool force)
    {
        if (_updateBusy || _popover is null || _updates is null) return;
        // Settings can be reopened frequently; respect the public GitHub API quota.
        if (!force && DateTimeOffset.UtcNow - _lastUpdateCheck < TimeSpan.FromMinutes(10)) return;
        _updateBusy = true;
        _popover.ShowUpdateState(_availableUpdate, "새 버전을 확인하는 중...", busy: true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_refreshCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            _availableUpdate = await _updates.CheckAsync(typeof(App).Assembly.GetName().Version!, timeout.Token);
            _lastUpdateCheck = DateTimeOffset.UtcNow;
            _popover.ShowUpdateState(_availableUpdate, _availableUpdate is null
                ? "최신 버전을 사용하고 있습니다." : "버튼을 누르면 다운로드 후 설치하고 자동으로 다시 실행합니다.");
        }
        catch (OperationCanceledException) when (_refreshCancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            _popover.ShowUpdateState(_availableUpdate, $"업데이트 확인 실패 · {error.Message}", isError: true);
            LogSettingsError($"update check: {error.Message}");
        }
        finally { _updateBusy = false; }
    }

    private async void Popover_OnUpdateInstall(object? sender, EventArgs e)
    {
        if (_updateBusy || _availableUpdate is null || _updates is null || _popover is null || _updatePaths is null) return;
        _updateBusy = true;
        var release = _availableUpdate;
        _popover.ShowUpdateState(release, "업데이트 다운로드 중...", busy: true, progress: 0);
        try
        {
            var executable = Environment.ProcessPath ?? throw new IOException("실행 파일 경로를 확인하지 못했습니다.");
            if (!WindowsUpdateInstaller.IsInstalledExecutable(executable))
                throw new InvalidOperationException("간편 업데이트는 설치 버전에서 사용할 수 있습니다. 먼저 설치 파일로 설치하십시오.");
            var lastPercent = -1;
            var progress = new Progress<double>(value =>
            {
                var percent = (int)(value * 100);
                if (percent == lastPercent) return;
                lastPercent = percent;
                _popover.ShowUpdateState(release, $"업데이트 다운로드 중 · {percent}%", busy: true, progress: value);
            });
            var package = await _updates.DownloadAsync(release, Path.Combine(_updatePaths.CacheDirectory, "Updates"), progress, _refreshCancellation.Token);
            _popover.ShowUpdateState(release, "진행을 저장하고 업데이트를 적용하는 중...", busy: true, progress: 1);
            _usageTimer?.Stop();
            _companionStore!.Persist();
            if (_companionStore.LastPersistenceError is { } error) throw new IOException("진행을 저장하지 못해 업데이트를 시작하지 않았습니다. " + error);
            await WindowsUpdateInstaller.LaunchAsync(package, release, executable, _updatePaths.DataDirectory,
                Environment.ProcessId, _refreshCancellation.Token);
            ExitApplication();
        }
        catch (OperationCanceledException) when (_refreshCancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            _usageTimer?.Start();
            _popover.ShowUpdateState(release, $"업데이트 실패 · {error.Message}", isError: true);
            LogSettingsError($"update install: {error.Message}");
        }
        finally { _updateBusy = false; }
    }
}
