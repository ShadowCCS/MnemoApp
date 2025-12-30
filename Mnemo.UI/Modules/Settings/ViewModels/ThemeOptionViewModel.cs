using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using System.Collections.Generic;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class ThemeOptionViewModel : ViewModelBase
{
    private readonly ThemeSettingViewModel _parent;
    
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _displayName;
    [ObservableProperty] private List<string> _previewColors;
    [ObservableProperty] private bool _isSelected;

    public ThemeOptionViewModel(ThemeManifest manifest, bool isSelected, ThemeSettingViewModel parent)
    {
        _name = manifest.Name;
        _displayName = manifest.DisplayName;
        _previewColors = manifest.PreviewColors;
        _isSelected = isSelected;
        _parent = parent;
    }

    [RelayCommand]
    private async Task Select()
    {
        await _parent.SelectThemeAsync(Name);
    }
}

