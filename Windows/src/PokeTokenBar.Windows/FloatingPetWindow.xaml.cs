using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using PokeTokenBar.Core;

namespace PokeTokenBar.Windows;

public partial class FloatingPetWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private bool _positionInitialized;
    private bool _applyingPosition;

    public FloatingPetWindow()
    {
        InitializeComponent();
        StartIdleAnimation();
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? HideRequested;

    public event Action<double, double>? PositionChanged;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(
            handle,
            GwlExStyle,
            new IntPtr(style | WsExNoActivate | WsExToolWindow));
    }

    public void ApplySettings(AppSettings settings)
    {
        var visualSize = settings.FloatingPetSize;
        Width = visualSize + 16;
        Height = visualSize + 16;

        if (!_positionInitialized)
        {
            _applyingPosition = true;
            try
            {
                if (settings.FloatingPetLeft is { } left
                    && settings.FloatingPetTop is { } top)
                {
                    Left = left;
                    Top = top;
                }
                else
                {
                    var workArea = SystemParameters.WorkArea;
                    Left = workArea.Right - Width - 24;
                    Top = workArea.Bottom - Height - 24;
                }

                KeepInsideVisibleWorkArea();
                _positionInitialized = true;
            }
            finally
            {
                _applyingPosition = false;
            }
        }
        else
        {
            KeepInsideVisibleWorkArea();
        }

        if (settings.FloatingPetEnabled)
        {
            if (!IsVisible)
            {
                Show();
            }
        }
        else
        {
            Hide();
        }
    }

    public void UpdateCompanionState(bool hasActivePokemon, string tooltip)
    {
        ToolTip = tooltip;
        if (!hasActivePokemon)
        {
            PetImage.Source = null;
            PetImage.Visibility = Visibility.Collapsed;
            EggVisual.Visibility = Visibility.Visible;
            FallbackText.Visibility = Visibility.Collapsed;
            return;
        }

        EggVisual.Visibility = Visibility.Collapsed;
        PetImage.Visibility = PetImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        FallbackText.Visibility = PetImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetPokemonSprite(byte[]? spriteBytes)
    {
        if (spriteBytes is null)
        {
            PetImage.Source = null;
            PetImage.Visibility = Visibility.Collapsed;
            FallbackText.Visibility = Visibility.Visible;
            return;
        }

        using var stream = new MemoryStream(spriteBytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        PetImage.Source = bitmap;
        PetImage.Visibility = Visibility.Visible;
        FallbackText.Visibility = Visibility.Collapsed;
    }

    private void PetHitSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var startLeft = Left;
        var startTop = Top;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        KeepInsideVisibleWorkArea();
        if (FloatingPetInteraction.IsClick(startLeft, startTop, Left, Top))
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (!_applyingPosition)
        {
            PositionChanged?.Invoke(Left, Top);
        }
    }

    private void PetHitSurface_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var menu = new System.Windows.Controls.ContextMenu();
        var hide = new System.Windows.Controls.MenuItem { Header = "플로팅 펫 끄기" };
        hide.Click += (_, _) => HideRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(hide);
        menu.PlacementTarget = PetHitSurface;
        menu.IsOpen = true;
    }

    private void KeepInsideVisibleWorkArea()
    {
        var workArea = CurrentWorkAreaInDips();
        var position = FloatingPetInteraction.ClampToWorkArea(
            Left,
            Top,
            Width,
            Height,
            new FloatingPetBounds(workArea.Left, workArea.Top, workArea.Right, workArea.Bottom));
        Left = position.Left;
        Top = position.Top;
    }

    private Rect CurrentWorkAreaInDips()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero
            || PresentationSource.FromVisual(this)?.CompositionTarget is not { } composition)
        {
            return SystemParameters.WorkArea;
        }

        var pixels = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var topLeft = composition.TransformFromDevice.Transform(
            new System.Windows.Point(pixels.Left, pixels.Top));
        var bottomRight = composition.TransformFromDevice.Transform(
            new System.Windows.Point(pixels.Right, pixels.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void StartIdleAnimation()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var transform = new TranslateTransform();
        PetVisualRoot.RenderTransform = transform;
        var bob = new DoubleAnimation(-3, 3, TimeSpan.FromMilliseconds(1_400))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        transform.BeginAnimation(TranslateTransform.YProperty, bob);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newValue);
}
