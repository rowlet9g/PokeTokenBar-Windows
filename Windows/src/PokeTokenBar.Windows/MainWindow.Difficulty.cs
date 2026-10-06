using System.Windows;

using PokeTokenBar.Core;

namespace PokeTokenBar.Windows;

public partial class MainWindow
{
    private bool _difficultyControlsReady;
    private double _draftGrowthDifficulty = 1;
    private double _draftShopDifficulty = 1;

    private void ApplyDifficultyControls(AppSettings settings, AppSettings previous)
    {
        // An unrelated immediate setting must not save or erase slider drafts.
        if (!_difficultyControlsReady || settings.GrowthDifficulty != previous.GrowthDifficulty
            || settings.ShopDifficulty != previous.ShopDifficulty)
        {
            _draftGrowthDifficulty = settings.GrowthDifficulty;
            _draftShopDifficulty = settings.ShopDifficulty;
            GrowthDifficultySlider.Value = PokemonBalance.DifficultyPosition(_draftGrowthDifficulty);
            ShopDifficultySlider.Value = PokemonBalance.DifficultyPosition(_draftShopDifficulty);
        }
        _difficultyControlsReady = true;
        UpdateDifficultyDraftUI();
    }

    private void DifficultySlider_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_difficultyControlsReady || _applyingSettings) return;
        _draftGrowthDifficulty = PokemonBalance.DifficultyAtPosition(GrowthDifficultySlider.Value);
        _draftShopDifficulty = PokemonBalance.DifficultyAtPosition(ShopDifficultySlider.Value);
        UpdateDifficultyDraftUI();
    }

    private void UpdateDifficultyDraftUI()
    {
        GrowthDifficultyText.Text = _draftGrowthDifficulty.ToString("0.#%");
        ShopDifficultyText.Text = _draftShopDifficulty.ToString("0.#%");
        SaveDifficultyButton.IsEnabled = _draftGrowthDifficulty != _currentSettings.GrowthDifficulty
            || _draftShopDifficulty != _currentSettings.ShopDifficulty;
    }

    private void SaveDifficultyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowSettingsStatus("난이도를 저장하는 중...");
        SettingsChanged?.Invoke(_currentSettings with
        {
            GrowthDifficulty = _draftGrowthDifficulty,
            ShopDifficulty = _draftShopDifficulty,
        });
    }
}
