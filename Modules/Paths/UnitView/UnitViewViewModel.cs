using MnemoApp.Core.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using MnemoApp.Data.Runtime;
using MnemoApp.Core.Tasks;
using MnemoApp.Core.MnemoAPI;
using System.Linq;

namespace MnemoApp.Modules.Paths.UnitView;

public partial class UnitViewViewModel : ViewModelBase
{
    private readonly IRuntimeStorage _storage;
    private readonly IMnemoAPI _mnemoAPI;
    private readonly string _pathId;
    private readonly int _unitOrder;
    
    [ObservableProperty]
    private string _content = string.Empty;
    
    [ObservableProperty]
    private string _unitTitle = string.Empty;
    
    [ObservableProperty]
    private string _pathTitle = string.Empty;

    public UnitViewViewModel(IRuntimeStorage storage, IMnemoAPI mnemoAPI, string pathId, int unitOrder)
    {
        _storage = storage;
        _mnemoAPI = mnemoAPI;
        _pathId = pathId;
        _unitOrder = unitOrder;
        
        LoadUnitContent();
        TriggerNextUnitGeneration();
    }

    public UnitViewViewModel(IRuntimeStorage storage, string pathId, int unitOrder)
        : this(storage, null!, pathId, unitOrder)
    {
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

    private void TriggerNextUnitGeneration()
    {
        if (_mnemoAPI == null) return;

        var pathKey = $"Content/Paths/{_pathId}";
        var pathData = _storage.GetProperty<PathData>(pathKey);

        if (pathData?.Units == null) return;

        // Find the next unit
        var nextUnitOrder = _unitOrder + 1;
        var nextUnit = pathData.Units.FirstOrDefault(u => u.Order == nextUnitOrder);

        // If next unit exists and doesn't have content, schedule generation
        if (nextUnit != null && string.IsNullOrWhiteSpace(nextUnit.Content))
        {
            System.Diagnostics.Debug.WriteLine($"[UNIT_VIEW] Triggering generation for next unit {nextUnitOrder}");
            _mnemoAPI.tasks.scheduleGenerateUnit(_pathId, nextUnitOrder);
        }
    }
}

