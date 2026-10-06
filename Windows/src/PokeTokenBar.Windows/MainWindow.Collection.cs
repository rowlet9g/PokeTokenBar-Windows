using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PokeTokenBar.Core;
using Image = System.Windows.Controls.Image;

namespace PokeTokenBar.Windows;

public partial class MainWindow
{
    private bool _collectionControlsReady;
    private bool _updatingCollectionControls;
    private bool _showCatchLog;
    private int _dexPage;
    private int? _selectedDexSpeciesId;
    private DexSort _dexSort;
    private CatchLogSort _catchLogSort;

    private void InitializeCollectionControls()
    {
        UpdateCollectionSortMenu();
        _collectionControlsReady = true;
    }

    private void UpdateCollectionSortMenu()
    {
        _updatingCollectionControls = true;
        try
        {
            CollectionSortComboBox.Items.Clear();
            var names = _showCatchLog ? Enum.GetNames<CatchLogSort>() : Enum.GetNames<DexSort>();
            foreach (var name in names)
                CollectionSortComboBox.Items.Add(new ComboBoxItem { Tag = name, Content = name switch
                {
                    "RecentFirst" => "최신순", "OldestFirst" => "오래된순",
                    "NumberAscending" => "번호 ↑", "NumberDescending" => "번호 ↓",
                    "NameAscending" => "이름 ↑", "NameDescending" => "이름 ↓", _ => "희귀도 ↓",
                } });
            var selected = _showCatchLog ? _catchLogSort.ToString() : _dexSort.ToString();
            CollectionSortComboBox.SelectedItem = CollectionSortComboBox.Items.Cast<ComboBoxItem>()
                .First(item => item.Tag.ToString() == selected);
        }
        finally { _updatingCollectionControls = false; }
    }

    private void CollectionFilters_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_collectionControlsReady || _updatingCollectionControls) return;
        ResetCollectionPage();
    }

    private void CollectionSort_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_collectionControlsReady || _updatingCollectionControls
            || CollectionSortComboBox.SelectedItem is not ComboBoxItem item) return;
        if (_showCatchLog) _catchLogSort = Enum.Parse<CatchLogSort>(item.Tag.ToString()!);
        else _dexSort = Enum.Parse<DexSort>(item.Tag.ToString()!);
        ResetCollectionPage();
    }

    private void ResetCollectionPage()
    {
        _dexPage = 0;
        _selectedDexSpeciesId = null;
        _pokedexSignature = null;
        SpeciesScrollView.ScrollToTop();
        CatchLogScrollView.ScrollToTop();
        ApplyPokedexState();
    }

    private void ResetCollectionFilters_OnClick(object sender, RoutedEventArgs e)
    {
        _updatingCollectionControls = true;
        try
        {
            CollectionSearchTextBox.Text = "";
            CollectionRarityComboBox.SelectedIndex = 0;
            CollectionShinyOnlyCheckBox.IsChecked = false;
            _dexSort = DexSort.NumberAscending;
            _catchLogSort = CatchLogSort.RecentFirst;
            UpdateCollectionSortMenu();
        }
        finally { _updatingCollectionControls = false; }
        ResetCollectionPage();
    }

    private void DexPreviousPage_OnClick(object sender, RoutedEventArgs e) => ChangeDexPage(-1);
    private void DexNextPage_OnClick(object sender, RoutedEventArgs e) => ChangeDexPage(1);
    private void ChangeDexPage(int delta)
    {
        _dexPage += delta;
        _selectedDexSpeciesId = null;
        _pokedexSignature = null;
        SpeciesScrollView.ScrollToTop();
        ApplyPokedexState();
    }

    private void SetRepresentative_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedDexSpeciesId is not { } id) return;
        if (!_companionStore.SetRepresentativeSpecies(_companionStore.RepresentativeSpeciesId == id ? null : id))
        {
            SelectedSpeciesText.Text = "대표 선택을 저장하지 못했습니다. 다시 시도하세요.";
            return;
        }
        ApplyPokedexState();
    }

    private void FollowCurrent_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_companionStore.SetRepresentativeSpecies(null))
        {
            RepresentativeStatusText.Text = "자동 추적 설정을 저장하지 못했습니다.";
            RepresentativeSettingsText.Text = RepresentativeStatusText.Text;
            return;
        }
        ApplyPokedexState();
    }

    private void ChooseRepresentative_OnClick(object sender, RoutedEventArgs e)
    {
        ResetCollectionFilters_OnClick(sender, e);
        ShowCollectionMode(false);
        ShowTab(MainTab.Pokedex);
    }

    private void ApplyPokedexState()
    {
        var allSpecies = _companionStore.DexSpecies;
        var allEntries = _companionStore.CollectionEntries;
        var query = CollectionSearchTextBox.Text;
        var rarityTag = (CollectionRarityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        PokemonRarity? rarity = Enum.TryParse<PokemonRarity>(rarityTag, out var parsed) ? parsed : null;
        var shinyOnly = CollectionShinyOnlyCheckBox.IsChecked == true;
        var species = CollectionQuery.Species(allSpecies, query, rarity, shinyOnly, _dexSort);
        var entries = CollectionQuery.Entries(allEntries, query, rarity, shinyOnly, _catchLogSort);
        var page = CollectionQuery.Page(species, _dexPage);
        _dexPage = page.Index;
        if (!page.Items.Any(item => item.SpeciesId == _selectedDexSpeciesId)) _selectedDexSpeciesId = null;
        var selected = page.Items.FirstOrDefault(item => item.SpeciesId == _selectedDexSpeciesId);
        var representative = _companionStore.Representative;
        var pinned = representative.IsPinned;
        PokedexCountText.Text = $"{allSpecies.Count}종 · {allEntries.Count}마리";
        DexPageText.Text = $"{page.Index + 1} / {page.PageCount}";
        DexPreviousPageButton.IsEnabled = page.Index > 0;
        DexNextPageButton.IsEnabled = page.Index < page.PageCount - 1;
        DexPageControls.Visibility = _showCatchLog ? Visibility.Collapsed : Visibility.Visible;
        SpeciesSelectionPanel.Visibility = _showCatchLog ? Visibility.Collapsed : Visibility.Visible;
        SetRepresentativeButton.IsEnabled = selected is not null;
        SetRepresentativeButton.Content = selected?.SpeciesId == _companionStore.RepresentativeSpeciesId && pinned ? "고정 해제" : "대표 지정";
        SelectedSpeciesText.Text = selected is null ? $"검색 결과 {species.Count}종 · 포켓몬을 선택하세요"
            : $"#{selected.SpeciesId:000} {selected.Name} · {RarityName(selected.Rarity)}";
        RepresentativeStatusText.Text = pinned ? $"대표: {representative.Name}" : "현재 포켓몬 자동 추적";
        RepresentativeSettingsText.Text = pinned
            ? $"{representative.Name} 고정 · 트레이와 플로팅 포켓몬에 표시합니다. 육성 대상은 홈에서 확인할 수 있습니다."
            : "현재 키우는 포켓몬이나 알을 트레이와 플로팅 포켓몬에 표시합니다.";
        FollowCurrentButton.IsEnabled = FollowCurrentSettingsButton.IsEnabled = pinned;
        var noResults = _showCatchLog ? entries.Count == 0 : species.Count == 0;
        EmptyPokedexView.Visibility = noResults ? Visibility.Visible : Visibility.Collapsed;
        EmptyPokedexTitleText.Text = allSpecies.Count == 0 ? "아직 만난 포켓몬이 없습니다" : "검색 결과가 없습니다";
        EmptyPokedexHintText.Text = allSpecies.Count == 0 ? "알이 부화하면 첫 기록이 생깁니다" : "검색어나 필터를 바꾸거나 초기화하세요";

        var signature = $"{_showCatchLog}:{_dexPage}:{_selectedDexSpeciesId}:{representative}:{shinyOnly};"
            + string.Join('|', page.Items.Select(item => item.ToString()))
            + string.Join('|', entries.Select(item => $"{item.Id}:{item.FinalSpeciesId}:{string.Join(',', item.ChainOrder)}:"
                + $"{item.CaughtAt?.UtcTicks}:{item.IsShiny}:{item.Nature}:{item.IsRaising}:{item.IsReleased}:"
                + string.Join(',', item.Names.OrderBy(pair => pair.Key))));
        if (signature == _pokedexSignature) return;
        _pokedexSignature = signature;
        var generation = Interlocked.Increment(ref _pokedexGeneration);
        PokedexItemsPanel.Children.Clear();
        CatchLogItemsPanel.Children.Clear();
        var targets = new List<SpriteTarget>();
        if (!_showCatchLog)
        {
            foreach (var item in page.Items)
            {
                var image = CreateSpriteImage(40);
                var selectedShiny = item.IsShiny && (shinyOnly || !item.HasNormal || item.SpeciesId == _selectedDexSpeciesId);
                targets.Add(new(item.SpeciesId, selectedShiny, image));
                PokedexItemsPanel.Children.Add(CreatePokedexCard(item, image));
            }
        }
        else
        {
            if (entries.Count > 0) CatchLogItemsPanel.Children.Add(CreateCatchLogSummary(entries));
            foreach (var entry in entries) CatchLogItemsPanel.Children.Add(CreateCatchLogCard(entry, targets));
        }
        _ = LoadCollectionSpritesAsync(targets, generation);
    }

    private Border CreatePokedexCard(PokemonDexSpecies item, Image image)
    {
        var pinned = item.SpeciesId == _companionStore.RepresentativeSpeciesId;
        var selected = item.SpeciesId == _selectedDexSpeciesId;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = $"#{item.SpeciesId:000}{(item.IsShiny ? " ★" : "")}{(item.IsRaising ? " · 육성" : "")}",
            FontSize = 8, Foreground = Brush(item.IsRaising ? "#FF007AFF" : "#FF81858C"), TextAlignment = TextAlignment.Center });
        panel.Children.Add(image);
        panel.Children.Add(new TextBlock { Text = item.Name, FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#FF202124"), TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var card = new Border { Height = 70, Margin = new Thickness(2), Padding = new Thickness(2),
            Background = Brush(pinned ? "#FFE0ECFA" : "#FFF0F1F3"), BorderBrush = Brush(selected ? "#FF007AFF" : "#FFD9DADD"),
            BorderThickness = new Thickness(selected ? 1.5 : 1), CornerRadius = new CornerRadius(7), Child = panel,
            Cursor = System.Windows.Input.Cursors.Hand, Focusable = true, ToolTip = $"#{item.SpeciesId:000} {item.Name} · {RarityName(item.Rarity)}"
                + (pinned ? " · 대표 포켓몬" : "") + (item.IsShiny ? " · 선택하면 이로치 색상 표시" : "") };
        System.Windows.Automation.AutomationProperties.SetName(card, card.ToolTip.ToString());
        void Select()
        {
            _selectedDexSpeciesId = item.SpeciesId;
            _pokedexSignature = null;
            ApplyPokedexState();
        }
        card.MouseLeftButtonUp += (_, e) => { Select(); e.Handled = true; };
        card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Select(); e.Handled = true; } };
        return card;
    }
}
