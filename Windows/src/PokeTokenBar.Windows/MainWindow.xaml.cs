using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokeTokenBar.Core;
using PokeTokenBar.Platform.Windows;
using Image = System.Windows.Controls.Image;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace PokeTokenBar.Windows;

public partial class MainWindow : Window
{
    private readonly UsageStore _usageStore;
    private readonly CompanionStore _companionStore;
    private readonly PokemonSpriteStore _spriteStore;
    private readonly CancellationToken _applicationToken;
    private int _pokedexGeneration;
    private string? _pokedexSignature;
    private bool _hideOnDeactivate = true;

    public MainWindow(
        WindowsAppPaths paths,
        UsageStore usageStore,
        CompanionStore companionStore,
        PokemonSpriteStore spriteStore,
        CancellationToken applicationToken)
    {
        _usageStore = usageStore;
        _companionStore = companionStore;
        _spriteStore = spriteStore;
        _applicationToken = applicationToken;
        InitializeComponent();
        StoragePathText.Text = paths.DataDirectory;
        ApplyUsageState();
        ApplyCompanionState();
    }

    public event EventHandler? RefreshRequested;

    public void ApplyUsageState()
    {
        var today = _usageStore.TodayTotalTokens;
        TokenValueText.Text = TokenFormatter.Compact(today);
        ExactTokenValueText.Text = TokenFormatter.Grouped(today);
        WeekValueText.Text = TokenFormatter.Compact(_usageStore.WeekTotalTokens);
        MonthValueText.Text = TokenFormatter.Compact(_usageStore.MonthTotalTokens);
        RefreshButton.IsEnabled = !_usageStore.IsRefreshing;

        if (_usageStore.IsRefreshing)
        {
            StatusText.Text = "Codex 로그를 읽는 중...";
            return;
        }

        if (_usageStore.LastErrorDescription is { } error)
        {
            StatusText.Text = $"새로고침 실패 · {error}";
            return;
        }

        if (_usageStore.Snapshots.Count == 0)
        {
            StatusText.Text = _usageStore.LastUpdated is null
                ? "Codex 로그 연결 준비 중"
                : "오늘 기록된 Codex 사용량이 없습니다";
            return;
        }

        StatusText.Text = _usageStore.LastUpdated is { } updated
            ? $"Codex · {updated.LocalDateTime:HH:mm:ss} 갱신"
            : "Codex";
    }

    public void ApplyCompanionState()
    {
        ApplyEvolutionLine();
        ApplyPokedexState();
        if (_companionStore.HasActivePokemon)
        {
            ApplyPokemonState();
            return;
        }

        EggVisual.Visibility = Visibility.Visible;
        PokemonImage.Visibility = Visibility.Collapsed;
        var progress = _companionStore.EggProgress;
        var percent = (int)Math.Round(progress * 100);
        CompanionProgressBar.Value = progress;
        CompanionProgressBar.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 90, 60));
        CompanionTitleText.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 128, 107));
        CompanionTitleText.Text = $"새 알 · {percent}%";

        if (!_companionStore.InstallBaselineSet)
        {
            CompanionProgressText.Text = "첫 사용량 동기화 후 부화를 시작합니다";
            return;
        }

        if (_companionStore.ReadyToHatch)
        {
            CompanionTitleText.Text = "새 알 · 부화 준비 완료";
            CompanionProgressText.Text = "다음 단계에서 포켓몬을 만나게 됩니다";
            return;
        }

        CompanionProgressText.Text = _companionStore.EggStarted
            ? $"{TokenFormatter.Compact(_companionStore.EggTokensToHatch)} 토큰 후 부화"
            : "다음 사용량부터 알이 자라기 시작합니다";
    }

    public void SetPokemonSprite(byte[]? bytes)
    {
        if (bytes is null)
        {
            PokemonImage.Source = null;
            return;
        }

        PokemonImage.Source = CreateBitmap(bytes);
    }

    private void ApplyEvolutionLine()
    {
        var stages = _companionStore.CurrentLineStages;
        EvolutionLinePanel.Children.Clear();
        EvolutionLinePanel.Visibility = stages.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        for (var index = 0; index < stages.Count; index++)
        {
            if (index > 0)
            {
                EvolutionLinePanel.Children.Add(new TextBlock
                {
                    Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brush("#FF596273"),
                    FontSize = 10,
                    Text = "›",
                });
            }

            var stage = stages[index];
            var isCurrent = stage.Status == PokemonLineStageStatus.Current;
            EvolutionLinePanel.Children.Add(new Border
            {
                Padding = new Thickness(7, 3, 7, 3),
                Background = Brush(isCurrent ? "#FF263E4B" : "#FF252A34"),
                BorderBrush = Brush(isCurrent ? "#FF7DD3FC" : "#FF343A46"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = new TextBlock
                {
                    Foreground = Brush(stage.Status == PokemonLineStageStatus.HiddenFuture
                        ? "#FF7D8797"
                        : isCurrent ? "#FF7DD3FC" : "#FFAEB7C7"),
                    FontSize = 9,
                    FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                    Text = stage.Name,
                },
            });
        }
    }

    private void ApplyPokedexState()
    {
        var species = _companionStore.DexSpecies;
        var collectionCount = _companionStore.CollectionEntries.Count;
        PokedexCountText.Text = $"{species.Count}종 · {collectionCount}마리";
        EmptyPokedexView.Visibility = species.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var signature = string.Join(
            '|',
            species.Select(item => $"{item.SpeciesId}:{item.Name}:{item.IsShiny}:{item.IsRaising}"));
        if (string.Equals(signature, _pokedexSignature, StringComparison.Ordinal))
        {
            return;
        }

        _pokedexSignature = signature;
        var generation = Interlocked.Increment(ref _pokedexGeneration);
        PokedexItemsPanel.Children.Clear();
        var imageTargets = new Dictionary<int, Image>();
        foreach (var item in species)
        {
            var image = new Image
            {
                Width = 62,
                Height = 62,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            imageTargets[item.SpeciesId] = image;
            PokedexItemsPanel.Children.Add(CreatePokedexCard(item, image));
        }

        _ = LoadPokedexSpritesAsync(species, imageTargets, generation);
    }

    private static Border CreatePokedexCard(PokemonDexSpecies item, Image image)
    {
        var panel = new StackPanel();
        panel.Children.Add(image);
        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 1, 0, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Foreground = MediaBrushes.White,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            MaxWidth = 88,
            Text = $"{(item.IsShiny ? "★ " : string.Empty)}{item.Name}",
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Foreground = item.IsRaising ? Brush("#FF7DD3FC") : Brush("#FF7D8797"),
            FontSize = 9,
            Text = item.IsRaising
                ? $"#{item.SpeciesId:000} · 육성 중"
                : $"#{item.SpeciesId:000} · {RarityName(item.Rarity)}",
        });

        return new Border
        {
            Width = 104,
            Height = 108,
            Margin = new Thickness(3),
            Padding = new Thickness(5),
            Background = Brush(item.IsRaising ? "#FF202A33" : "#FF20242C"),
            BorderBrush = Brush(item.IsRaising ? "#FF3C8DAA" : "#FF343A46"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = panel,
        };
    }

    private async Task LoadPokedexSpritesAsync(
        IReadOnlyList<PokemonDexSpecies> species,
        IReadOnlyDictionary<int, Image> imageTargets,
        int generation)
    {
        try
        {
            foreach (var item in species)
            {
                byte[]? bytes;
                try
                {
                    bytes = await _spriteStore.GetSpriteAsync(
                        item.SpeciesId,
                        item.IsShiny,
                        _applicationToken);
                }
                catch (OperationCanceledException) when (_applicationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    continue;
                }

                if (bytes is null
                    || generation != Volatile.Read(ref _pokedexGeneration)
                    || _applicationToken.IsCancellationRequested)
                {
                    continue;
                }

                if (imageTargets.TryGetValue(item.SpeciesId, out var image))
                {
                    image.Source = CreateBitmap(bytes);
                }
            }
        }
        catch (OperationCanceledException) when (_applicationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch
        {
            // Cached text records remain useful when the sprite host is unavailable.
        }
    }

    private static BitmapImage CreateBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static SolidColorBrush Brush(string value) =>
        new((MediaColor)MediaColorConverter.ConvertFromString(value));

    private static string RarityName(PokemonRarity rarity) => rarity switch
    {
        PokemonRarity.Common => "일반",
        PokemonRarity.Uncommon => "특별",
        PokemonRarity.Rare => "희귀",
        PokemonRarity.Legendary => "전설",
        _ => rarity.ToString(),
    };

    private void ApplyPokemonState()
    {
        EggVisual.Visibility = Visibility.Collapsed;
        PokemonImage.Visibility = Visibility.Visible;
        var progress = _companionStore.GrowthProgress;
        var percent = (int)Math.Round(progress * 100);
        CompanionProgressBar.Value = progress;
        var shiny = _companionStore.IsCurrentPokemonShiny;
        var accent = shiny
            ? MediaColor.FromRgb(250, 204, 21)
            : MediaColor.FromRgb(125, 211, 252);
        CompanionProgressBar.Foreground = new SolidColorBrush(accent);
        CompanionTitleText.Foreground = new SolidColorBrush(accent);
        CompanionTitleText.Text = $"{(shiny ? "★ " : string.Empty)}{_companionStore.CurrentPokemonName} · {percent}%";

        var destination = _companionStore.CurrentStage < _companionStore.TotalForms
            ? "진화"
            : "졸업";
        CompanionProgressText.Text =
            $"{_companionStore.CurrentStage}/{_companionStore.TotalForms}단계 · "
            + $"{TokenFormatter.Compact(_companionStore.TokensToNextStage)} 토큰 후 {destination}";
    }

    public void ShowNearNotificationArea(bool hideOnDeactivate = true)
    {
        _hideOnDeactivate = hideOnDeactivate;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - Height - 12;
        Show();
        Activate();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_hideOnDeactivate)
        {
            Hide();
        }
    }

    private void HideButton_OnClick(object sender, RoutedEventArgs e)
    {
        _hideOnDeactivate = true;
        Hide();
    }

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HomeTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowTab(showPokedex: false);
    }

    private void PokedexTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowTab(showPokedex: true);
    }

    private void ShowTab(bool showPokedex)
    {
        HomeView.Visibility = showPokedex ? Visibility.Collapsed : Visibility.Visible;
        PokedexView.Visibility = showPokedex ? Visibility.Visible : Visibility.Collapsed;
        HomeTabButton.Background = Brush(showPokedex ? "#00171A21" : "#FF2B3440");
        HomeTabButton.Foreground = Brush(showPokedex ? "#FF7D8797" : "#FFFFFFFF");
        PokedexTabButton.Background = Brush(showPokedex ? "#FF2B3440" : "#00171A21");
        PokedexTabButton.Foreground = Brush(showPokedex ? "#FFFFFFFF" : "#FF7D8797");
    }
}
