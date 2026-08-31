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
using WpfOrientation = System.Windows.Controls.Orientation;

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
        ApplyPersistenceStatus();
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

    private void ApplyPersistenceStatus()
    {
        if (_companionStore.LastPersistenceError is not { } error)
        {
            PersistenceErrorText.Visibility = Visibility.Visible;
            PersistenceErrorText.Foreground = Brush("#FF697386");
            PersistenceErrorText.Text = $"State · {_companionStore.StateLoadDescription}";
            PersistenceErrorText.ToolTip = _companionStore.StateFilePath;
            return;
        }

        PersistenceErrorText.Visibility = Visibility.Visible;
        PersistenceErrorText.Foreground = Brush("#FFFF806B");
        PersistenceErrorText.Text = $"저장 상태 오류 · {error}";
        PersistenceErrorText.ToolTip = error;
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
        var entries = _companionStore.CollectionEntries;
        PokedexCountText.Text = $"{species.Count}종 · {entries.Count}마리";
        EmptyPokedexView.Visibility = entries.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var speciesSignature = string.Join('|', species.Select(item =>
            $"{item.SpeciesId}:{item.Name}:{item.IsShiny}:{item.IsRaising}"));
        var entrySignature = string.Join('|', entries.Select(item =>
            $"{item.Id}:{item.FinalSpeciesId}:{string.Join(',', item.ChainOrder)}:"
            + $"{item.CaughtAt?.UtcTicks}:{item.IsShiny}:{item.Nature}:{item.IsRaising}"));
        var signature = $"{speciesSignature};;{entrySignature}";
        if (string.Equals(signature, _pokedexSignature, StringComparison.Ordinal))
        {
            return;
        }

        _pokedexSignature = signature;
        var generation = Interlocked.Increment(ref _pokedexGeneration);
        PokedexItemsPanel.Children.Clear();
        CatchLogItemsPanel.Children.Clear();
        var imageTargets = new List<SpriteTarget>();
        foreach (var item in species)
        {
            var image = CreateSpriteImage(62);
            imageTargets.Add(new SpriteTarget(item.SpeciesId, item.IsShiny, image));
            PokedexItemsPanel.Children.Add(CreatePokedexCard(item, image));
        }

        if (entries.Count > 0)
        {
            CatchLogItemsPanel.Children.Add(CreateCatchLogSummary(entries));
        }

        foreach (var entry in entries)
        {
            CatchLogItemsPanel.Children.Add(CreateCatchLogCard(entry, imageTargets));
        }

        _ = LoadCollectionSpritesAsync(imageTargets, generation);
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

    private static Border CreateCatchLogSummary(IReadOnlyList<PokemonCollectionEntry> entries)
    {
        var panel = new WrapPanel { HorizontalAlignment = WpfHorizontalAlignment.Left };
        foreach (var rarity in Enum.GetValues<PokemonRarity>())
        {
            var count = entries.Count(entry => entry.Rarity == rarity);
            panel.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(7, 3, 7, 3),
                Background = Brush("#FF20242C"),
                BorderBrush = RarityBrush(rarity),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Opacity = count == 0 ? 0.4 : 1,
                Child = new TextBlock
                {
                    Foreground = RarityBrush(rarity),
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Text = $"{RarityName(rarity)} {count}",
                },
            });
        }

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel,
        };
    }

    private static Border CreateCatchLogCard(
        PokemonCollectionEntry entry,
        ICollection<SpriteTarget> imageTargets)
    {
        var content = new StackPanel();
        var header = new DockPanel();
        var natureText = new TextBlock
        {
            Foreground = Brush("#FF8F98A8"),
            FontSize = 9,
            Text = NatureName(entry.Nature),
        };
        DockPanel.SetDock(natureText, Dock.Right);
        header.Children.Add(natureText);
        var badges = new StackPanel { Orientation = WpfOrientation.Horizontal };
        badges.Children.Add(CreateBadge(RarityName(entry.Rarity), RarityBrush(entry.Rarity)));
        if (entry.IsRaising)
        {
            badges.Children.Add(CreateBadge("육성 중", Brush("#FF7DD3FC")));
        }
        if (entry.IsShiny)
        {
            badges.Children.Add(new TextBlock
            {
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("#FFFACC15"),
                FontSize = 11,
                Text = "★",
            });
        }
        header.Children.Add(badges);
        content.Children.Add(header);

        var chain = new StackPanel
        {
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Orientation = WpfOrientation.Horizontal,
        };
        for (var index = 0; index < entry.ChainOrder.Count; index++)
        {
            if (index > 0)
            {
                chain.Children.Add(new TextBlock
                {
                    Margin = new Thickness(2, 0, 2, 12),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brush("#FF596273"),
                    FontSize = 13,
                    Text = "›",
                });
            }

            var speciesId = entry.ChainOrder[index];
            var image = CreateSpriteImage(50);
            imageTargets.Add(new SpriteTarget(speciesId, entry.IsShiny, image));
            var stage = new StackPanel { Width = 82 };
            stage.Children.Add(image);
            stage.Children.Add(new TextBlock
            {
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                Foreground = Brush("#FFAEB7C7"),
                FontSize = 9,
                MaxWidth = 78,
                Text = entry.Names.TryGetValue(speciesId, out var name) ? name : $"#{speciesId}",
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            chain.Children.Add(stage);
        }
        content.Children.Add(chain);
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = Brush("#FF697386"),
            FontSize = 9,
            Text = entry.IsRaising ? "현재 함께 성장하는 중" : RelativeCaughtAt(entry.CaughtAt),
        });

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(8),
            Background = Brush(entry.IsRaising ? "#FF202A33" : "#FF20242C"),
            BorderBrush = Brush(entry.IsRaising ? "#FF3C8DAA" : "#FF343A46"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = content,
        };
    }

    private static Border CreateBadge(string text, SolidColorBrush accent) => new()
    {
        Margin = new Thickness(0, 0, 5, 0),
        Padding = new Thickness(6, 2, 6, 2),
        Background = new SolidColorBrush(MediaColor.FromArgb(35, accent.Color.R, accent.Color.G, accent.Color.B)),
        CornerRadius = new CornerRadius(7),
        Child = new TextBlock
        {
            Foreground = accent,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Text = text.ToUpperInvariant(),
        },
    };

    private static Image CreateSpriteImage(double size)
    {
        var image = new Image
        {
            Width = size,
            Height = size,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Stretch = Stretch.Uniform,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    private async Task LoadCollectionSpritesAsync(
        IReadOnlyList<SpriteTarget> imageTargets,
        int generation)
    {
        try
        {
            foreach (var target in imageTargets)
            {
                byte[]? bytes;
                try
                {
                    bytes = await _spriteStore.GetSpriteAsync(
                        target.SpeciesId,
                        target.IsShiny,
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

                target.Image.Source = CreateBitmap(bytes);
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

    private sealed record SpriteTarget(int SpeciesId, bool IsShiny, Image Image);

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

    private static SolidColorBrush RarityBrush(PokemonRarity rarity) => rarity switch
    {
        PokemonRarity.Common => Brush("#FF94A3B8"),
        PokemonRarity.Uncommon => Brush("#FF34D399"),
        PokemonRarity.Rare => Brush("#FFA78BFA"),
        PokemonRarity.Legendary => Brush("#FFF59E0B"),
        _ => Brush("#FF94A3B8"),
    };

    private static string NatureName(PokemonNature nature) => nature switch
    {
        PokemonNature.Hardy => "노력하는 성격",
        PokemonNature.Lonely => "외로움을 타는 성격",
        PokemonNature.Brave => "용감한 성격",
        PokemonNature.Adamant => "고집 센 성격",
        PokemonNature.Naughty => "개구쟁이 성격",
        PokemonNature.Bold => "대담한 성격",
        PokemonNature.Docile => "온순한 성격",
        PokemonNature.Relaxed => "무사태평한 성격",
        PokemonNature.Impish => "장난꾸러기 성격",
        PokemonNature.Lax => "촐랑거리는 성격",
        PokemonNature.Timid => "겁쟁이 성격",
        PokemonNature.Hasty => "성급한 성격",
        PokemonNature.Serious => "성실한 성격",
        PokemonNature.Jolly => "명랑한 성격",
        PokemonNature.Naive => "천진난만한 성격",
        PokemonNature.Modest => "조심스러운 성격",
        PokemonNature.Mild => "의젓한 성격",
        PokemonNature.Quiet => "냉정한 성격",
        PokemonNature.Bashful => "수줍음을 타는 성격",
        PokemonNature.Rash => "덜렁거리는 성격",
        PokemonNature.Calm => "차분한 성격",
        PokemonNature.Gentle => "얌전한 성격",
        PokemonNature.Sassy => "건방진 성격",
        PokemonNature.Careful => "신중한 성격",
        PokemonNature.Quirky => "변덕스러운 성격",
        _ => nature.ToString(),
    };

    private static string RelativeCaughtAt(DateTimeOffset? caughtAt)
    {
        if (caughtAt is null)
        {
            return "획득 시각 기록 없음";
        }

        var elapsed = DateTimeOffset.UtcNow - caughtAt.Value.ToUniversalTime();
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "방금 졸업";
        }
        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)}분 전 졸업";
        }
        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)}시간 전 졸업";
        }
        if (elapsed < TimeSpan.FromDays(7))
        {
            return $"{Math.Max(1, (int)elapsed.TotalDays)}일 전 졸업";
        }

        return $"{caughtAt.Value.LocalDateTime:yyyy.MM.dd} 졸업";
    }

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

    private void SpeciesModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowCollectionMode(showCatchLog: false);
    }

    private void CatchLogModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowCollectionMode(showCatchLog: true);
    }

    private void ShowCollectionMode(bool showCatchLog)
    {
        SpeciesScrollView.Visibility = showCatchLog ? Visibility.Collapsed : Visibility.Visible;
        CatchLogScrollView.Visibility = showCatchLog ? Visibility.Visible : Visibility.Collapsed;
        SpeciesModeButton.Background = Brush(showCatchLog ? "#00171A21" : "#FF2B3440");
        SpeciesModeButton.Foreground = Brush(showCatchLog ? "#FF7D8797" : "#FFFFFFFF");
        CatchLogModeButton.Background = Brush(showCatchLog ? "#FF2B3440" : "#00171A21");
        CatchLogModeButton.Foreground = Brush(showCatchLog ? "#FFFFFFFF" : "#FF7D8797");
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
