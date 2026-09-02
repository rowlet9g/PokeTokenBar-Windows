using System.Threading;

namespace PokeTokenBar.Platform.Windows;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private bool _disposed;

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $"{name}.Activate");
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public void StartListening(Action activationRequested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(activationRequested);
        if (!IsPrimaryInstance)
        {
            throw new InvalidOperationException(
                "Only the primary application instance can listen for activation.");
        }

        if (_activationRegistration is not null)
        {
            throw new InvalidOperationException("Activation listening already started.");
        }

        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut && !_disposed)
                {
                    activationRequested();
                }
            },
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void SignalPrimary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _activationEvent.Set();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activationRegistration?.Unregister(waitObject: null);
        _activationRegistration = null;
        _activationEvent.Dispose();
        _mutex.Dispose();
    }
}
