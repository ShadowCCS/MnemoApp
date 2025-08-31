using System.Threading.Tasks;
using System.Windows.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.MnemoAPI;
using CommunityToolkit.Mvvm.Input;

namespace MnemoApp.Modules.Settings;

public class SettingsViewModel : ViewModelBase
{
    private readonly IMnemoAPI _mnemoAPI;

    public ICommand ToggleThemeCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public SettingsViewModel(IMnemoAPI mnemoAPI)
    {
        _mnemoAPI = mnemoAPI;
        ToggleThemeCommand = new AsyncRelayCommand(ToggleTheme);
        ToggleLanguageCommand = new AsyncRelayCommand(ToggleLanguage);
    }

    private async Task ToggleTheme()
    {
        var result = await _mnemoAPI.ui.overlay.Show<string?>("UI/Components/Overlays/ThemeSelectOverlay.axaml", name: "ThemeSelectOverlay");
        if (result != null)
        {
            await _mnemoAPI.ui.themes.setTheme(result);
        }
    }

    private async Task ToggleLanguage()
    {
        var result = await _mnemoAPI.ui.overlay.Show<string?>("UI/Components/Overlays/LanguageSelectOverlay.axaml", name: "LanguageSelectOverlay");
        if (!string.IsNullOrWhiteSpace(result))
        {
            await _mnemoAPI.ui.language.setLanguage(result!);
        }
    }
}
