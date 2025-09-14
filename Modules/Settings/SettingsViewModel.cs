using System.Threading.Tasks;
using System.Windows.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.MnemoAPI;
using CommunityToolkit.Mvvm.Input;
using Tmds.DBus.Protocol;

namespace MnemoApp.Modules.Settings;

public class SettingsViewModel : ViewModelBase
{
    private readonly IMnemoAPI _mnemoAPI;

    public ICommand ToggleThemeCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public string CurrentTheme => _mnemoAPI.ui.themes.getCurrentTheme()?.Name ?? "Error";
    public string CurrentLanguage => _mnemoAPI.ui.language.getCurrentLanguageManifest()?.NativeName ?? "Error";

    public string Message { get; set; } = "";
    public ICommand SendMessageCommand { get; }
    public ICommand RefreshModelsCommand { get; }
    // AI model selection - delegated to MnemoAPI
    public System.Collections.ObjectModel.ObservableCollection<string> AvailableModels => _mnemoAPI.ai.GetAvailableModelsObservable();
    public string? CurrentModel
    {
        get => _mnemoAPI.ai.GetSelectedModel();
        set => _mnemoAPI.ai.SetSelectedModel(value);
    }

    public SettingsViewModel(IMnemoAPI mnemoAPI)
    {
        _mnemoAPI = mnemoAPI;
        ToggleThemeCommand = new AsyncRelayCommand(ToggleTheme);
        ToggleLanguageCommand = new AsyncRelayCommand(ToggleLanguage);
        SendMessageCommand = new AsyncRelayCommand(SendMessage);
        RefreshModelsCommand = new AsyncRelayCommand(RefreshModels);
        // Subscribe to model selection changes to update UI
        _mnemoAPI.ai.SubscribeToSelectedModelChanges(_ => OnPropertyChanged(nameof(CurrentModel)));
    }

    private async Task ToggleTheme()
    {
        var result = await _mnemoAPI.ui.overlay.Show<string?>("UI/Components/Overlays/ThemeSelectOverlay.axaml", name: "ThemeSelectOverlay");
        if (result != null)
        {
            await _mnemoAPI.ui.themes.setTheme(result);
        }
        OnPropertyChanged(nameof(CurrentTheme));
    }

    private async Task ToggleLanguage()
    {
        var result = await _mnemoAPI.ui.overlay.Show<string?>("UI/Components/Overlays/LanguageSelectOverlay.axaml", name: "LanguageSelectOverlay");
        if (!string.IsNullOrWhiteSpace(result))
        {
            await _mnemoAPI.ui.language.setLanguage(result!);
            OnPropertyChanged(nameof(CurrentLanguage));
        }
    }

    private async Task SendMessage()
    {
        var request = _mnemoAPI.ai.CreateRequestWithSelectedModel(Message);
        if (request == null)
        {
            Message = "Error: No model selected";
            OnPropertyChanged(nameof(Message));
            return;
        }

        var response = await _mnemoAPI.ai.InferAsync(request);
        if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
        {
            Message = "Error: " + response.ErrorMessage;
        }
        else
        {
            Message = response.Response ?? string.Empty;
        }
        OnPropertyChanged(nameof(Message));
    }

    private async Task RefreshModels()
    {
        await _mnemoAPI.ai.RefreshAsync();
        OnPropertyChanged(nameof(AvailableModels));
    }

    }
