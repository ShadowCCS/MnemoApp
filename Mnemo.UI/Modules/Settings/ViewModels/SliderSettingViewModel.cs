using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class SliderSettingViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly string _settingsKey;

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _description;
    [ObservableProperty] private double _value;
    [ObservableProperty] private double _minimum;
    [ObservableProperty] private double _maximum;
    [ObservableProperty] private double _increment;

    public SliderSettingViewModel(ISettingsService settingsService, string settingsKey, string title, string description, double min, double max, double increment, double defaultValue)
    {
        _settingsService = settingsService;
        _settingsKey = settingsKey;
        _title = title;
        _description = description;
        _minimum = min;
        _maximum = max;
        _increment = increment;
        
        var savedValue = _settingsService.GetAsync(_settingsKey, defaultValue).GetAwaiter().GetResult();
        _value = savedValue;
    }

    partial void OnValueChanged(double value)
    {
        _settingsService.SetAsync(_settingsKey, value).ConfigureAwait(false);
    }
}
