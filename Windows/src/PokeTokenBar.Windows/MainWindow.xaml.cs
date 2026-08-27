using System.Windows;
using PokeTokenBar.Core;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Windows;

public partial class MainWindow : Window
{
    public MainWindow(WindowsAppPaths paths)
    {
        InitializeComponent();
        StoragePathText.Text = paths.DataDirectory;
        TokenValueText.Text = TokenFormatter.Compact(0);
    }

    public void ShowNearNotificationArea()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - Height - 12;
        Show();
        Activate();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        Hide();
    }

    private void HideButton_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
