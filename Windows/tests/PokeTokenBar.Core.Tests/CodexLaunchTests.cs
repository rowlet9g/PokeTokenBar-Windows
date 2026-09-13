using System.Diagnostics;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class CodexLaunchTests
{
    [Fact]
    public void Native_launcher_uses_default_stdio_transport()
    {
        var startInfo = CodexRateLimitsProvider.CreateStartInfo(@"C:\Program Files\Codex\codex.exe");

        Assert.Equal(new[] { "app-server" }, startInfo.ArgumentList);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
    }

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
            // Like the Codex CLI, reject unsupported extra arguments.
            File.WriteAllText(path,
                "@echo off\r\nif not \"%~2\"==\"\" exit /b 2\r\necho %1\r\n");
            using var process = Process.Start(CodexRateLimitsProvider.CreateStartInfo(path))!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("app-server", output.Trim());
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
