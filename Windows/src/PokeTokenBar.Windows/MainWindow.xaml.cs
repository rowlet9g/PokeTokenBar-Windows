using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
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
    private bool _hasUserPosition;
    private bool _applyingSettings;
    private AppSettings _currentSettings = new();
    private CompanionMilestone? _pendingMilestone;
    private CompanionItemKind? _pendingPurchase;
    private FreshEggTier? _pendingEggPurchase;
    private bool _pendingEggShinyConfirmed;
    private CompanionItemKind? _pendingUse;

    public MainWindow(
        UsageStore usageStore,
        CompanionStore companionStore,
        PokemonSpriteStore spriteStore,
        AppSettings settings,
        CancellationToken applicationToken)
    {
        _usageStore = usageStore;
        _companionStore = companionStore;
        _spriteStore = spriteStore;
        _applicationToken = applicationToken;
        InitializeComponent();
        IsVisibleChanged += MainWindow_OnIsVisibleChanged;
        ApplySettings(settings);
        ApplyUsageState();
        ApplyCompanionState();
    }

    public event EventHandler? RefreshRequested;

    public event Action<AppSettings>? SettingsChanged;

    public void ApplySettings(AppSettings settings)
    {
        _applyingSettings = true;
        try
        {
            _currentSettings = settings;
            NotificationsCheckBox.IsChecked = settings.NotificationsEnabled;
            AlwaysOnTopCheckBox.IsChecked = settings.AlwaysOnTop;
            LaunchAtLoginCheckBox.IsChecked = settings.LaunchAtLogin;
            FloatingPetCheckBox.IsChecked = settings.FloatingPetEnabled;
            Topmost = settings.AlwaysOnTop;

            var selected = RefreshIntervalComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(),
                    settings.RefreshIntervalMinutes.ToString(),
                    StringComparison.Ordinal));
            RefreshIntervalComboBox.SelectedItem = selected
                ?? RefreshIntervalComboBox.Items.OfType<ComboBoxItem>()
                    .First(item => string.Equals(item.Tag?.ToString(), "2", StringComparison.Ordinal));

            var selectedPetSize = FloatingPetSizeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(),
                    settings.FloatingPetSize.ToString(),
                    StringComparison.Ordinal));
            FloatingPetSizeComboBox.SelectedItem = selectedPetSize
                ?? FloatingPetSizeComboBox.Items.OfType<ComboBoxItem>()
                    .First(item => string.Equals(item.Tag?.ToString(), "96", StringComparison.Ordinal));
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    public void ShowSettingsStatus(string message, bool isError = false)
    {
        SettingsStatusText.Foreground = Brush(isError ? "#FFFF806B" : "#FF7DD3FC");
        SettingsStatusText.Text = message;
    }

    public void QueueMilestoneAnimation(CompanionMilestone milestone)
    {
        _pendingMilestone = milestone;
        if (IsVisible)
        {
            PlayPendingMilestoneAnimation();
        }
    }

    private void MainWindow_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            StartIdleAnimation();
            PlayPendingMilestoneAnimation();
        }
        else
        {
            CompanionIdleTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            CompanionIdleTranslate.Y = 0;
        }
    }

    private void StartIdleAnimation()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var bob = new DoubleAnimation(
            fromValue: -2,
            toValue: 2,
            duration: TimeSpan.FromMilliseconds(1_300))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        CompanionIdleTranslate.BeginAnimation(TranslateTransform.YProperty, bob);
    }

    private void PlayPendingMilestoneAnimation()
    {
        if (_pendingMilestone is not { } milestone)
        {
            return;
        }

        _pendingMilestone = null;
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(milestone.PokemonName)
            ? "포켓몬"
            : milestone.PokemonName;
        var shiny = milestone.IsShiny ? "이로치 " : string.Empty;
        (MilestoneGlyphText.Text, MilestoneTitleText.Text, MilestoneDetailText.Text) = milestone.Kind switch
        {
            CompanionMilestoneKind.Hatched => ("✦", "새로운 포켓몬!", $"{shiny}{name} 부화"),
            CompanionMilestoneKind.Evolved => ("★", "진화 성공!", $"새로운 모습 · {shiny}{name}"),
            CompanionMilestoneKind.Graduated => ("✓", "도감 등록 완료!", $"{shiny}{name} 육성 완료"),
            _ => throw new ArgumentOutOfRangeException(),
        };

        MilestoneOverlay.Visibility = Visibility.Visible;
        MilestoneOverlay.Opacity = 0;
        MilestoneOverlayScale.ScaleX = 0.88;
        MilestoneOverlayScale.ScaleY = 0.88;

        var fade = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(2_400),
            FillBehavior = FillBehavior.Stop,
        };
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.18)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.78)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        fade.Completed += (_, _) =>
        {
            MilestoneOverlay.Visibility = Visibility.Collapsed;
            MilestoneOverlay.BeginAnimation(OpacityProperty, null);
            MilestoneOverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            MilestoneOverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        };

        var scale = new DoubleAnimation(
            fromValue: 0.88,
            toValue: 1,
            duration: TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new BackEase
            {
                Amplitude = 0.32,
                EasingMode = EasingMode.EaseOut,
            },
        };
        MilestoneOverlay.BeginAnimation(OpacityProperty, fade);
        MilestoneOverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        MilestoneOverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    public void ApplyUsageState()
    {
        var today = _usageStore.TodayTotalTokens;
        TokenValueText.Text = TokenFormatter.Compact(today);
        ExactTokenValueText.Text = TokenFormatter.Grouped(today);
        WeekValueText.Text = TokenFormatter.Compact(_usageStore.WeekTotalTokens);
        MonthValueText.Text = TokenFormatter.Compact(_usageStore.MonthTotalTokens);
        RefreshButton.IsEnabled = !_usageStore.IsRefreshing;
        ApplyTokensState();

        if (_usageStore.IsRefreshing)
        {
            StatusText.Text = "로컬 AI 로그를 읽는 중...";
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
                ? "로컬 사용량 연결 준비 중"
                : "오늘 기록된 로컬 AI 사용량이 없습니다";
            return;
        }

        var providers = string.Join(" + ", _usageStore.Snapshots.Select(snapshot => snapshot.DisplayName));
        StatusText.Text = _usageStore.LastUpdated is { } updated
            ? $"{providers} · {updated.LocalDateTime:HH:mm:ss} 갱신"
            : providers;
    }

    public void ApplyCompanionState()
    {
        ApplyEvolutionLine();
        ApplyPokedexState();
        ApplyInventoryState();
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
        var eggTitle = _companionStore.EggGuarantee switch
        {
            PokemonRarity.Uncommon => "고급 이상 알",
            PokemonRarity.Rare => "희귀 이상 알",
            _ => "새 알",
        };
        CompanionTitleText.Text = $"{eggTitle} · {percent}%";

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

    private void ApplyInventoryState()
    {
        ShopWalletText.Text = TokenFormatter.Compact(_companionStore.AvailableTokens);
        ShopItemsPanel.Children.Clear();
        foreach (var kind in Enum.GetValues<CompanionItemKind>()
                     .OrderBy(CompanionItemRules.Price))
        {
            ShopItemsPanel.Children.Add(CreateShopItemCard(kind));
        }
        if (_companionStore.HasActivePokemon)
        {
            foreach (var tier in Enum.GetValues<FreshEggTier>())
            {
                ShopItemsPanel.Children.Add(CreateFreshEggCard(tier));
            }
        }

        BagItemsPanel.Children.Clear();
        foreach (var item in _companionStore.OwnedItems)
        {
            BagItemsPanel.Children.Add(CreateBagItemCard(item));
        }

        EmptyBagView.Visibility = _companionStore.OwnedItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private Border CreateShopItemCard(CompanionItemKind kind)
    {
        var owned = _companionStore.ItemCount(kind);
        var passiveOwned = CompanionItemRules.IsPassive(kind) && owned > 0;
        var button = new System.Windows.Controls.Button
        {
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            Style = (Style)FindResource("ModernActionButton"),
            Tag = kind,
            Content = passiveOwned
                ? "보유 중"
                : _companionStore.CanBuyItem(kind) ? "구매" : "잔액 부족",
            IsEnabled = !passiveOwned && _companionStore.CanBuyItem(kind),
        };
        button.Click += ShopBuyButton_OnClick;

        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(button, Dock.Right);
        footer.Children.Add(button);
        footer.Children.Add(new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#FF8F98A8"),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 10,
            Text = $"가격 {TokenFormatter.Compact(CompanionItemRules.Price(kind))}"
                + (owned > 0 && !CompanionItemRules.IsPassive(kind) ? $" · 보유 {owned}" : string.Empty),
        });

        var header = new StackPanel { Orientation = WpfOrientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Width = 34,
            VerticalAlignment = VerticalAlignment.Top,
            FontSize = 23,
            Text = ItemEmoji(kind),
        });
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Foreground = MediaBrushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Text = ItemName(kind),
        });
        copy.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = Brush("#FF8F98A8"),
            FontSize = 9,
            Text = ItemDescription(kind),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 250,
        });
        header.Children.Add(copy);

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(footer);
        return ItemCard(content);
    }

    private Border CreateBagItemCard(OwnedCompanionItem item)
    {
        var header = new DockPanel();
        var count = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#FF7DD3FC"),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Text = CompanionItemRules.IsPassive(item.Kind) ? "적용 중" : $"×{item.Count}",
        };
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(count);
        header.Children.Add(new TextBlock
        {
            Foreground = MediaBrushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Text = $"{ItemEmoji(item.Kind)}  {ItemName(item.Kind)}",
        });

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brush("#FF8F98A8"),
            FontSize = 9,
            Text = ItemDescription(item.Kind),
            TextWrapping = TextWrapping.Wrap,
        });
        if (!CompanionItemRules.IsPassive(item.Kind))
        {
            var button = new System.Windows.Controls.Button
            {
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = WpfHorizontalAlignment.Right,
                Style = (Style)FindResource("ModernActionButton"),
                Tag = item.Kind,
                Content = "사용",
                IsEnabled = item.Kind switch
                {
                    CompanionItemKind.RareCandy => _companionStore.CanUseRareCandy,
                    CompanionItemKind.Mint => _companionStore.CanUseMint,
                    _ => false,
                },
            };
            button.Click += BagUseButton_OnClick;
            content.Children.Add(button);
        }

        return ItemCard(content);
    }

    private Border CreateFreshEggCard(FreshEggTier tier)
    {
        var button = new System.Windows.Controls.Button
        {
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            Style = (Style)FindResource("ModernActionButton"),
            Tag = tier,
            Content = _companionStore.CanBuyFreshEgg(tier) ? "구매" : "잔액 부족",
            IsEnabled = _companionStore.CanBuyFreshEgg(tier),
        };
        button.Click += FreshEggBuyButton_OnClick;

        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(button, Dock.Right);
        footer.Children.Add(button);
        footer.Children.Add(new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#FF8F98A8"),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 10,
            Text = $"가격 {TokenFormatter.Compact(CompanionItemRules.FreshEggPrice(tier))}",
        });

        var header = new StackPanel { Orientation = WpfOrientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Width = 34,
            VerticalAlignment = VerticalAlignment.Top,
            FontSize = 23,
            Text = "🥚",
        });
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Foreground = MediaBrushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Text = FreshEggName(tier),
        });
        copy.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = Brush("#FF8F98A8"),
            FontSize = 9,
            Text = FreshEggDescription(tier),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 250,
        });
        header.Children.Add(copy);

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(footer);
        return ItemCard(content);
    }

    private static Border ItemCard(UIElement content) => new()
    {
        Margin = new Thickness(0, 0, 0, 8),
        Padding = new Thickness(10),
        Background = Brush("#FF20242C"),
        BorderBrush = Brush("#FF343A46"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Child = content,
    };

    private static string ItemName(CompanionItemKind kind) => kind switch
    {
        CompanionItemKind.RareCandy => "이상한 사탕",
        CompanionItemKind.Mint => "민트",
        CompanionItemKind.ShinyCharm => "이로치 부적",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ItemEmoji(CompanionItemKind kind) => kind switch
    {
        CompanionItemKind.RareCandy => "🍬",
        CompanionItemKind.Mint => "🌿",
        CompanionItemKind.ShinyCharm => "✨",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ItemDescription(CompanionItemKind kind) => kind switch
    {
        CompanionItemKind.RareCandy => "현재 포켓몬에게 100M 성장 경험치를 줍니다.",
        CompanionItemKind.Mint => "현재 포켓몬의 성격을 다른 성격으로 변경합니다.",
        CompanionItemKind.ShinyCharm => "향후 부화하는 포켓몬의 이로치 확률을 1/48로 높입니다.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string FreshEggName(FreshEggTier tier) => tier switch
    {
        FreshEggTier.Basic => "새 알",
        FreshEggTier.Uncommon => "고급 이상 알",
        FreshEggTier.Rare => "희귀 이상 알",
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    private static string FreshEggDescription(FreshEggTier tier) => tier switch
    {
        FreshEggTier.Basic => "현재 포켓몬을 놓아주고 보증 없는 새 알을 받습니다.",
        FreshEggTier.Uncommon => "현재 포켓몬을 놓아주고 고급 이상 등급을 보증합니다.",
        FreshEggTier.Rare => "현재 포켓몬을 놓아주고 희귀 이상 등급을 보증합니다.",
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
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
        if (!_hasUserPosition)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 12;
            Top = workArea.Bottom - Height - 12;
        }
        else
        {
            KeepInsideNearestWorkArea();
        }
        Show();
        Activate();
    }

    private void HeaderDragRegion_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
            _hasUserPosition = true;
        }
        catch (InvalidOperationException)
        {
            // The mouse button was released before WPF began the drag operation.
        }
    }

    private void KeepInsideNearestWorkArea()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var center = new System.Drawing.Point(
            (int)Math.Round(Left + width / 2),
            (int)Math.Round(Top + height / 2));
        var workArea = System.Windows.Forms.Screen.FromPoint(center).WorkingArea;
        var maximumLeft = Math.Max(workArea.Left, workArea.Right - width);
        var maximumTop = Math.Max(workArea.Top, workArea.Bottom - height);
        Left = Math.Clamp(Left, workArea.Left, maximumLeft);
        Top = Math.Clamp(Top, workArea.Top, maximumTop);
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
        ShowTab(MainTab.Home);
    }

    private void PokedexTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowTab(MainTab.Pokedex);
    }

    private void SettingsTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowTab(MainTab.Settings);
    }

    private void ShopTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowTab(MainTab.Shop);
    }

    private void BagTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowTab(MainTab.Bag);
    }

    private void ShopBuyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: CompanionItemKind kind })
        {
            return;
        }

        _pendingPurchase = kind;
        _pendingEggPurchase = null;
        _pendingEggShinyConfirmed = false;
        ConfirmPurchaseButton.Content = "구매 확정";
        ShopConfirmationText.Text =
            $"{ItemName(kind)}을(를) {TokenFormatter.Compact(CompanionItemRules.Price(kind))} 토큰에 구매하겠습니까?";
        ShopConfirmationPanel.Visibility = Visibility.Visible;
    }

    private void ConfirmPurchaseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_pendingEggPurchase is { } eggTier)
        {
            if (_companionStore.IsCurrentPokemonShiny && !_pendingEggShinyConfirmed)
            {
                _pendingEggShinyConfirmed = true;
                ShopConfirmationText.Text =
                    "현재 포켓몬은 이로치입니다. 도감에 등록되지 않은 채 영구히 놓아주게 됩니다. 정말 계속하겠습니까?";
                ConfirmPurchaseButton.Content = "이로치 놓아주기";
                return;
            }

            _pendingEggPurchase = null;
            _pendingEggShinyConfirmed = false;
            ConfirmPurchaseButton.Content = "구매 확정";
            ShopConfirmationPanel.Visibility = Visibility.Collapsed;
            var purchasedEgg = _companionStore.BuyFreshEgg(eggTier);
            ShopStatusText.Text = purchasedEgg
                ? $"{FreshEggName(eggTier)} 구매 완료"
                : "새 알을 구매할 수 없습니다";
            if (purchasedEgg)
            {
                ShowTab(MainTab.Home);
            }
            ApplyInventoryState();
            return;
        }

        if (_pendingPurchase is not { } kind)
        {
            return;
        }

        _pendingPurchase = null;
        ShopConfirmationPanel.Visibility = Visibility.Collapsed;
        var purchased = _companionStore.BuyItem(kind);
        ShopStatusText.Text = purchased
            ? $"{ItemName(kind)} 구매 완료"
            : "구매할 수 없습니다";
        ApplyInventoryState();
    }

    private void CancelPurchaseButton_OnClick(object sender, RoutedEventArgs e)
    {
        _pendingPurchase = null;
        _pendingEggPurchase = null;
        _pendingEggShinyConfirmed = false;
        ConfirmPurchaseButton.Content = "구매 확정";
        ShopConfirmationPanel.Visibility = Visibility.Collapsed;
    }

    private void FreshEggBuyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: FreshEggTier tier })
        {
            return;
        }

        _pendingPurchase = null;
        _pendingEggPurchase = tier;
        _pendingEggShinyConfirmed = false;
        ConfirmPurchaseButton.Content = "구매 확정";
        ShopConfirmationText.Text =
            $"{_companionStore.CurrentPokemonName ?? "현재 포켓몬"}을(를) 놓아주고 "
            + $"{FreshEggName(tier)}을(를) {TokenFormatter.Compact(CompanionItemRules.FreshEggPrice(tier))} 토큰에 구매하겠습니까? "
            + "놓아준 포켓몬은 도감에 등록되지 않습니다.";
        ShopConfirmationPanel.Visibility = Visibility.Visible;
    }

    private void BagUseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: CompanionItemKind kind })
        {
            return;
        }

        _pendingUse = kind;
        BagConfirmationText.Text = $"현재 포켓몬에게 {ItemName(kind)}을(를) 사용하겠습니까?";
        BagConfirmationPanel.Visibility = Visibility.Visible;
    }

    private void ConfirmUseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_pendingUse is not { } kind)
        {
            return;
        }

        _pendingUse = null;
        BagConfirmationPanel.Visibility = Visibility.Collapsed;
        switch (kind)
        {
            case CompanionItemKind.RareCandy:
                var result = _companionStore.UseRareCandy();
                BagStatusText.Text = result == RareCandyUseResult.Unavailable
                    ? "지금은 이상한 사탕을 사용할 수 없습니다"
                    : "+100M 성장 경험치를 적용했습니다";
                if (result != RareCandyUseResult.Unavailable)
                {
                    ShowTab(MainTab.Home);
                    CompanionProgressText.Text = "+100M XP";
                }
                break;
            case CompanionItemKind.Mint:
                var nature = _companionStore.UseMint();
                BagStatusText.Text = nature is null
                    ? "지금은 민트를 사용할 수 없습니다"
                    : $"성격 변경 완료 · {NatureName(nature.Value)}";
                if (nature is not null)
                {
                    ShowTab(MainTab.Home);
                    CompanionProgressText.Text = $"성격 변경 · {NatureName(nature.Value)}";
                }
                break;
            case CompanionItemKind.ShinyCharm:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        ApplyInventoryState();
    }

    private void CancelUseButton_OnClick(object sender, RoutedEventArgs e)
    {
        _pendingUse = null;
        BagConfirmationPanel.Visibility = Visibility.Collapsed;
    }

    private void SettingsControls_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings
            || RefreshIntervalComboBox.SelectedItem is not ComboBoxItem intervalItem
            || !int.TryParse(intervalItem.Tag?.ToString(), out var intervalMinutes)
            || FloatingPetSizeComboBox.SelectedItem is not ComboBoxItem petSizeItem
            || !int.TryParse(petSizeItem.Tag?.ToString(), out var petSize))
        {
            return;
        }

        ShowSettingsStatus("설정을 저장하는 중...");
        SettingsChanged?.Invoke(_currentSettings with
        {
            RefreshIntervalMinutes = intervalMinutes,
            NotificationsEnabled = NotificationsCheckBox.IsChecked == true,
            AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true,
            LaunchAtLogin = LaunchAtLoginCheckBox.IsChecked == true,
            FloatingPetEnabled = FloatingPetCheckBox.IsChecked == true,
            FloatingPetSize = petSize,
        });
    }

    private void ExportSaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.Now;
        var dialog = new WpfSaveFileDialog
        {
            Title = "PokeTokenBar 세이브 내보내기",
            FileName = SaveTransfer.SuggestedFileName(now),
            DefaultExt = ".json",
            Filter = "PokeTokenBar save (*.json)|*.json",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            var bytes = SaveTransfer.Encode(
                _companionStore.ExportStateSnapshot(),
                version,
                Environment.MachineName,
                now);
            File.WriteAllBytes(dialog.FileName, bytes);
            ShowSettingsStatus($"세이브를 내보냈습니다 · {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowSettingsStatus($"내보내기 실패 · {error.Message}", isError: true);
        }
    }

    private async void ImportSaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "PokeTokenBar 세이브 가져오기",
            DefaultExt = ".json",
            Filter = "PokeTokenBar save (*.json)|*.json",
            Multiselect = false,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var envelope = SaveTransfer.Decode(SaveTransfer.ReadFile(dialog.FileName));
            var incoming = SaveSummary.From(envelope.State);
            var current = SaveSummary.From(_companionStore.ExportStateSnapshot());
            var answer = System.Windows.MessageBox.Show(
                this,
                $"현재 진행을 가져온 세이브로 교체합니다.\n\n"
                + $"현재: 누적 {TokenFormatter.Compact(current.LifetimeTokens)}, 도감 {current.DexCount}마리\n"
                + $"가져올 파일: 누적 {TokenFormatter.Compact(incoming.LifetimeTokens)}, 도감 {incoming.DexCount}마리\n"
                + $"출처: {envelope.SourceDevice} · {envelope.ExportedAt.LocalDateTime:yyyy-MM-dd HH:mm}\n\n"
                + "교체 직전 상태는 자동 백업됩니다.",
                "세이브 가져오기",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                ShowSettingsStatus("가져오기를 취소했습니다");
                return;
            }

            var todayTokens = _usageStore.TodayTokensByProvider;
            await _companionStore.ImportStateAsync(
                envelope.State,
                todayTokens,
                DateOnly.FromDateTime(DateTime.Now),
                hasUsageData: todayTokens.Count > 0,
                DateTimeOffset.Now,
                _applicationToken);
            ApplyCompanionState();
            ShowSettingsStatus("세이브를 가져왔습니다 · 이전 상태는 자동 백업됨");
        }
        catch (Exception error) when (error is SaveTransferException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or JsonException)
        {
            ShowSettingsStatus($"가져오기 실패 · {error.Message}", isError: true);
        }
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

    private void ShowTab(MainTab tab)
    {
        HomeView.Visibility = tab == MainTab.Home ? Visibility.Visible : Visibility.Collapsed;
        PokedexView.Visibility = tab == MainTab.Pokedex ? Visibility.Visible : Visibility.Collapsed;
        TokensView.Visibility = tab == MainTab.Tokens ? Visibility.Visible : Visibility.Collapsed;
        ShopView.Visibility = tab == MainTab.Shop ? Visibility.Visible : Visibility.Collapsed;
        BagView.Visibility = tab == MainTab.Bag ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = tab == MainTab.Settings ? Visibility.Visible : Visibility.Collapsed;
        SetTabStyle(HomeTabButton, tab == MainTab.Home);
        SetTabStyle(PokedexTabButton, tab == MainTab.Pokedex);
        SetTabStyle(TokensTabButton, tab == MainTab.Tokens);
        SetTabStyle(ShopTabButton, tab == MainTab.Shop);
        SetTabStyle(BagTabButton, tab == MainTab.Bag);
        SetTabStyle(SettingsTabButton, tab == MainTab.Settings);
    }

    private static void SetTabStyle(System.Windows.Controls.Button button, bool selected)
    {
        button.Background = Brush(selected ? "#FF2B3440" : "#00171A21");
        button.Foreground = Brush(selected ? "#FFFFFFFF" : "#FF7D8797");
    }

    private enum MainTab
    {
        Home,
        Pokedex,
        Tokens,
        Shop,
        Bag,
        Settings,
    }
}
