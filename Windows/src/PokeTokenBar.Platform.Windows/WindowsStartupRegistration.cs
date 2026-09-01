using Microsoft.Win32;

namespace PokeTokenBar.Platform.Windows;

public sealed class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PokeTokenBar";
    private readonly string _executablePath;

    public WindowsStartupRegistration(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _executablePath = Path.GetFullPath(executablePath);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("The Windows startup registry key is unavailable.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        if (!File.Exists(_executablePath))
        {
            throw new FileNotFoundException(
                "Install PokeTokenBar before enabling launch at login.",
                _executablePath);
        }

        key.SetValue(ValueName, BuildCommand(_executablePath), RegistryValueKind.String);
    }

    public static string BuildCommand(string executablePath) =>
        $"\"{Path.GetFullPath(executablePath)}\" --background";
}
