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
        try
        {
            var theme = _mnemoAPI.ui.themes.getCurrentTheme();
            System.Diagnostics.Debug.WriteLine($"Current theme: {theme?.Name ?? "null"}");
            
            bool success;
            if (theme?.Name == "Dawn")
            {
                System.Diagnostics.Debug.WriteLine("Switching to Dusk...");
                success = await _mnemoAPI.ui.themes.setTheme("Dusk");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Switching to Dawn...");
                success = await _mnemoAPI.ui.themes.setTheme("Dawn");
            }
            
            System.Diagnostics.Debug.WriteLine($"Theme switch success: {success}");
            
            var newTheme = _mnemoAPI.ui.themes.getCurrentTheme();
            System.Diagnostics.Debug.WriteLine($"New theme: {newTheme?.Name ?? "null"}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error toggling theme: {ex.Message}");
        }
    }
}
