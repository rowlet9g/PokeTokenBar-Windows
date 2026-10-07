using System.Diagnostics;
using System.Text;
using PokeTokenBar.Core;

namespace PokeTokenBar.Platform.Windows;

/// <summary>Starts a detached helper which waits for graceful exit before running Inno Setup.</summary>
public static class WindowsUpdateInstaller
{
    public static bool IsInstalledExecutable(string path) => File.Exists(Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(path))!, "unins000.exe"));

    public static async Task LaunchAsync(string installerPath, AppUpdateRelease release, string executablePath,
        string dataDirectory, int processId, CancellationToken token = default)
    {
        if (!IsInstalledExecutable(executablePath)) throw new InvalidOperationException("간편 업데이트는 설치 버전에서 사용할 수 있습니다. 먼저 설치 파일로 설치하십시오.");
        await AppUpdateClient.VerifyFileAsync(installerPath, release.Sha256, token).ConfigureAwait(false);
        var installerVersion = FileVersionInfo.GetVersionInfo(installerPath).ProductVersion?.Trim();
        if (installerVersion != release.Version.ToString()) throw new InvalidDataException("설치 파일 버전이 업데이트 정보와 다릅니다.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(installerPath))!;
        var scriptPath = Path.Combine(directory, "apply-update.ps1");
        var requestPath = Path.Combine(directory, "update-request.json");
        var request = new { installer = Path.GetFullPath(installerPath), executable = Path.GetFullPath(executablePath),
            dataDirectory = Path.GetFullPath(dataDirectory), processId, sha256 = release.Sha256, version = release.Version.ToString() };
        await File.WriteAllTextAsync(requestPath, System.Text.Json.JsonSerializer.Serialize(request), token).ConfigureAwait(false);
        // Windows PowerShell 5 needs a BOM to decode localized script messages as UTF-8.
        await File.WriteAllTextAsync(scriptPath, HelperScript, new UTF8Encoding(true), token).ConfigureAwait(false);
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-RequestPath", requestPath })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("업데이트 도우미를 실행하지 못했습니다.");
        var readyPath = Path.Combine(directory, "helper-ready.json");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!File.Exists(readyPath))
        {
            if (process.HasExited || DateTimeOffset.UtcNow >= deadline)
                throw new IOException("업데이트 도우미를 준비하지 못했습니다. 앱을 종료하지 않았습니다.");
            await Task.Delay(100, token).ConfigureAwait(false);
        }
    }

    // Paths are carried in JSON instead of interpolated PowerShell/shell text.
    public const string HelperScript = """
        param([Parameter(Mandatory=$true)][string]$RequestPath)
        $ErrorActionPreference='Stop'
        $request=Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json
        $resultPath=Join-Path ([IO.Path]::GetDirectoryName($RequestPath)) 'update-result.json'
        $logPath=Join-Path ([IO.Path]::GetDirectoryName($RequestPath)) 'installer.log'
        function Get-UpdateHash([string]$Path) {
            $stream=[IO.File]::OpenRead($Path)
            $sha=[Security.Cryptography.SHA256]::Create()
            try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
            finally { $sha.Dispose(); $stream.Dispose() }
        }
        try {
            [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetDirectoryName($RequestPath)) 'helper-ready.json'),'{"ready":true}')
            $oldProcess=Get-Process -Id $request.processId -ErrorAction SilentlyContinue
            if ($oldProcess -and $oldProcess.Path -ne $request.executable) { throw 'Update process identity changed.' }
            if ($oldProcess -and -not $oldProcess.WaitForExit(90000)) { throw 'App did not exit; installation was not started.' }
            $actualHash=Get-UpdateHash $request.installer
            if ($actualHash -ne $request.sha256) { throw 'Installer checksum changed.' }
            $backup=Join-Path $request.dataDirectory ('UpdateBackups\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $backup -Force | Out-Null
            $hashes=@{}
            foreach ($name in @('companion-state.json','settings.json')) {
                $path=Join-Path $request.dataDirectory $name
                if (Test-Path -LiteralPath $path) {
                    Copy-Item -LiteralPath $path -Destination (Join-Path $backup $name)
                    $hashes[$name]=Get-UpdateHash $path
                }
            }
            $installDirectory=[IO.Path]::GetDirectoryName($request.executable)
            $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$installDirectory+'"'),('/LOG="'+$logPath+'"'))
            $installer=Start-Process -FilePath $request.installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
            if ($installer.ExitCode -ne 0) { throw ('Installer exit code: '+$installer.ExitCode) }
            $version=[Diagnostics.FileVersionInfo]::GetVersionInfo($request.executable).FileVersion.Trim()
            if ($version -ne ($request.version+'.0')) { throw 'Installed executable version mismatch.' }
            foreach ($name in $hashes.Keys) {
                if ((Get-UpdateHash (Join-Path $request.dataDirectory $name)) -ne $hashes[$name]) {
                    throw ('Saved user data changed during installation: '+$name)
                }
            }
            @{success=$true;version=$request.version;backup=$backup} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
            Start-Process -FilePath $request.executable -WindowStyle Hidden
        } catch {
            @{success=$false;error=$_.Exception.Message} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
            Add-Type -AssemblyName PresentationFramework
            if (Test-Path -LiteralPath $request.executable) { Start-Process -FilePath $request.executable -WindowStyle Hidden }
            [System.Windows.MessageBox]::Show(('업데이트를 완료하지 못했습니다. 오류를 확인한 뒤 다시 시도하십시오.'+"`n`n"+$_.Exception.Message),'PokeTokenBar 업데이트') | Out-Null
        }
        """;
}
