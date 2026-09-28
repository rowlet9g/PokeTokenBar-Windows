using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PokeTokenBar.Platform.Windows;

internal static class AntigravityDesktopDiscovery
{
    internal static async Task<AntigravityDesktopConnection?> ResolveAsync(
        string? serverLogPath, CancellationToken cancellationToken)
    {
        if (serverLogPath is null || !File.Exists(serverLogPath)) return null;
        int port;
        try
        {
            using var stream = new FileStream(serverLogPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(Math.Max(0, stream.Length - 1024 * 1024), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var log = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var matches = Regex.Matches(log, @"listening on random port at (\d+) for HTTP\b");
            if (matches.Count == 0 || !int.TryParse(matches[^1].Groups[1].Value, out port)
                || port is < 1 or > 65535) return null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        // 2.17 no longer logs the launch arguments. Read only the local server's
        // connection credential and verify that it owns the logged loopback port.
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $servers = Get-CimInstance Win32_Process -Filter "Name='language_server.exe'"
            foreach ($server in $servers) {
                if ($server.SessionId -ne {{Process.GetCurrentProcess().SessionId}}) { continue }
                if ($server.ExecutablePath -notmatch '\\antigravity\\resources\\bin\\language_server\.exe$') { continue }
                $match = [regex]::Match($server.CommandLine, '--csrf_token(?:=|\s+)([^\s"]+)')
                if (-not $match.Success) { continue }
                $listener = Get-NetTCPConnection -State Listen -LocalPort {{port}} -ErrorAction SilentlyContinue |
                    Where-Object { $_.OwningProcess -eq $server.ProcessId -and $_.LocalAddress -eq '127.0.0.1' }
                if ($listener) {
                    @{ port = {{port}}; csrf = $match.Groups[1].Value } | ConvertTo-Json -Compress
                    break
                }
            }
            """;
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            if (!process.Start()) return null;
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var json = await stdout.ConfigureAwait(false);
            await stderr.ConfigureAwait(false); // Never log process arguments or credentials.
            return process.ExitCode == 0 ? ParseConnection(json) : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (IOException) { return null; }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    internal static AntigravityDesktopConnection? ParseConnection(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("port", out var portValue) || !portValue.TryGetInt32(out var port)
                || port is < 1 or > 65535 || !root.TryGetProperty("csrf", out var tokenValue)
                || tokenValue.ValueKind != JsonValueKind.String) return null;
            var token = tokenValue.GetString();
            return token is { Length: > 0 and <= 4096 } && !token.Any(char.IsWhiteSpace)
                ? new AntigravityDesktopConnection(port, token) : null;
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}
