using System.Diagnostics;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class CodexLaunchTests
{
    [Theory]
    [InlineData("cmd")]
    [InlineData("bat")]
    public async Task Batch_launcher_with_spaces_receives_app_server_arguments(string extension)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Ptb launch {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "codex." + extension);
        try
        {
            File.WriteAllText(path, "@echo off\r\necho %1 %2\r\n");
            using var process = Process.Start(CodexRateLimitsProvider.CreateStartInfo(path))!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("app-server --stdio", output.Trim());
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
