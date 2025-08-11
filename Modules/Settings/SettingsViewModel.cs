using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Services;
using CommunityToolkit.Mvvm.Input;

namespace MnemoApp.Modules.Settings;

public class SettingsViewModel : ViewModelBase
{
    private readonly IMnemoAPI _mnemoAPI;

    public ICommand ToggleThemeCommand { get; }
    public SettingsViewModel(IMnemoAPI mnemoAPI)
    {
        _mnemoAPI = mnemoAPI;
        ToggleThemeCommand = new AsyncRelayCommand(ToggleTheme);
    }

    private async Task ToggleTheme()
    {
        var result = await _mnemoAPI.ui.overlay.Show<string?>("UI/Components/Overlays/ThemeSelectOverlay.axaml", name: "ThemeSelectOverlay");
        if (result != null)
        {
            await _mnemoAPI.ui.themes.setTheme(result);
        }
    }
}
