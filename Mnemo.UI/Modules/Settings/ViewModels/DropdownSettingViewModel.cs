using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class DropdownSettingViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly string _settingsKey;

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _selectedOption;
    public ObservableCollection<string> Options { get; }

    public DropdownSettingViewModel(ISettingsService settingsService, string settingsKey, string title, string description, string[] options)
    {
        _settingsService = settingsService;
        _settingsKey = settingsKey;
        _title = title;
        _description = description;
        Options = new ObservableCollection<string>(options);
        
        // Initialize value from settings
        var savedValue = _settingsService.GetAsync(_settingsKey, options[0]).GetAwaiter().GetResult();
        _selectedOption = savedValue ?? options[0];
    }

    partial void OnSelectedOptionChanged(string value)
    {
        _settingsService.SetAsync(_settingsKey, value).ConfigureAwait(false);
    }
}

