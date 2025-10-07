using MnemoApp.Core.Common;
using System.Collections.ObjectModel;
using MnemoApp.Data.Runtime;
using MnemoApp.Core.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Modules.Paths.UnitView;

namespace MnemoApp.Modules.Paths.UnitOverview;

public class UnitOverviewViewModel : ViewModelBase
{
    private readonly IRuntimeStorage _storage;
    private readonly IMnemoAPI _mnemoAPI;
    private string _pathId;
    private string _pathTitle = string.Empty;
    
    public string PathTitle
    {
        get => _pathTitle;
        set => SetProperty(ref _pathTitle, value);
    }
    
    public ObservableCollection<UnitInfo> Units { get; } = new();
    public ICommand NavigateToUnitCommand { get; }
    
    public UnitOverviewViewModel(IRuntimeStorage storage, IMnemoAPI mnemoAPI, string pathId)
    {
        _storage = storage;
        _mnemoAPI = mnemoAPI;
        _pathId = pathId;
        
        NavigateToUnitCommand = new RelayCommand<UnitInfo>(NavigateToUnit);
        
        LoadPathAndUnits();
    }
    
    private void NavigateToUnit(UnitInfo? unitInfo)
    {
        if (unitInfo != null && unitInfo.IsEnabled)
        {
            var unitViewVm = new UnitViewViewModel(_storage, _pathId, unitInfo.Order);
            _mnemoAPI.navigate.Navigate(unitViewVm);
        }
    }
    
    private void LoadPathAndUnits()
    {
        Units.Clear();
        
        var pathKey = $"Content/Paths/{_pathId}";
        var pathData = _storage.GetProperty<PathData>(pathKey);
        
        if (pathData != null)
        {
            PathTitle = pathData.Title;
            
            if (pathData.Units != null)
            {
                foreach (var unit in pathData.Units)
                {
                    Units.Add(new UnitInfo
                    {
                        Order = unit.Order,
                        Title = unit.Title ?? string.Empty,
                        HasContent = !string.IsNullOrWhiteSpace(unit.Content),
                        IsEnabled = !string.IsNullOrWhiteSpace(unit.Content)
                    });
                }
            }
        }
    }
}

public class UnitInfo
{
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool HasContent { get; set; }
    public bool IsEnabled { get; set; }
}

