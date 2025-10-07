using MnemoApp.Core.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using MnemoApp.Data.Runtime;
using MnemoApp.Core.Tasks;
using System.Linq;

namespace MnemoApp.Modules.Paths.UnitView;

public partial class UnitViewViewModel : ViewModelBase
{
    private readonly IRuntimeStorage _storage;
    private readonly string _pathId;
    private readonly int _unitOrder;
    
    [ObservableProperty]
    private string _content = string.Empty;
    
    [ObservableProperty]
    private string _unitTitle = string.Empty;
    
    [ObservableProperty]
    private string _pathTitle = string.Empty;

    public UnitViewViewModel(IRuntimeStorage storage, string pathId, int unitOrder)
    {
        _storage = storage;
        _pathId = pathId;
        _unitOrder = unitOrder;
        
        LoadUnitContent();
    }
    
    private void LoadUnitContent()
    {
        var pathKey = $"Content/Paths/{_pathId}";
        var pathData = _storage.GetProperty<PathData>(pathKey);
        
        if (pathData != null)
        {
            PathTitle = pathData.Title;
            
            if (pathData.Units != null)
            {
                var unit = pathData.Units.FirstOrDefault(u => u.Order == _unitOrder);
                if (unit != null)
                {
                    UnitTitle = unit.Title ?? $"Unit {_unitOrder}";
                    Content = unit.Content ?? "# No content available";
                }
                else
                {
                    UnitTitle = $"Unit {_unitOrder}";
                    Content = "# Unit not found";
                }
            }
        }
    }
}

