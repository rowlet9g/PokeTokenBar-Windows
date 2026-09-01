using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class WindowsStartupRegistrationTests
{
    [Fact]
    public void Startup_command_quotes_the_executable_and_uses_background_mode()
    {
        const string executable = @"C:\Program Files\PokeTokenBar\PokeTokenBar.Windows.exe";

        var command = WindowsStartupRegistration.BuildCommand(executable);

        Assert.Equal($"\"{executable}\" --background", command);
    }
}
