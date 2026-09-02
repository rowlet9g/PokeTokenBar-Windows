using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public async Task Secondary_instance_signals_the_primary_instance()
    {
        var name = $"Local\\PokeTokenBar.Tests.{Guid.NewGuid():N}";
        using var primary = new SingleInstanceGuard(name);
        var activation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening(() => activation.TrySetResult());

        using var secondary = new SingleInstanceGuard(name);
        Assert.True(primary.IsPrimaryInstance);
        Assert.False(secondary.IsPrimaryInstance);

        secondary.SignalPrimary();

        await activation.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
