using System.Drawing;
using System.Windows.Forms;

namespace PokeTokenBar.Platform.Windows;

public sealed class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly Icon _defaultIcon;
    private Icon? _spriteIcon;
    private byte[]? _spriteBytes;
    private bool _disposed;

    public TrayIconController(Icon icon)
    {
        _defaultIcon = icon;
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("Open PokeTokenBar", image: null, (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty));
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Exit", image: null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "PokeTokenBar",
            ContextMenuStrip = _contextMenu,
            Visible = true,
        };

        _notifyIcon.MouseUp += OnMouseUp;
        _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;
    }

    public event EventHandler? ToggleRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? NotificationClicked;

    public void UpdatePokemonSprite(byte[]? bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (bytes is null && _spriteIcon is null) return;
        if (bytes is not null && _spriteBytes is not null && bytes.AsSpan().SequenceEqual(_spriteBytes)) return;
        var next = bytes is null ? null : PokemonTrayIcon.Create(bytes);
        _notifyIcon.Icon = next ?? _defaultIcon;
        var previous = _spriteIcon;
        _spriteIcon = next;
        _spriteBytes = bytes;
        previous?.Dispose();
    }

    public void UpdateTooltip(string text)
    {
        const int notifyIconTextLimit = 63;
        _notifyIcon.Text = text.Length <= notifyIconTextLimit
            ? text
            : text[..notifyIconTextLimit];
    }

    public void ShowNotification(string title, string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _notifyIcon.ShowBalloonTip(
            timeout: 5_000,
            tipTitle: title,
            tipText: message,
            tipIcon: ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.MouseUp -= OnMouseUp;
        _notifyIcon.BalloonTipClicked -= OnBalloonTipClicked;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _spriteIcon?.Dispose();
        _contextMenu.Dispose();
        _disposed = true;
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ToggleRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnBalloonTipClicked(object? sender, EventArgs e) =>
        NotificationClicked?.Invoke(this, EventArgs.Empty);
}
