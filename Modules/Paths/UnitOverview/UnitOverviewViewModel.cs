using MnemoApp.Core.Common;
using System.Collections.ObjectModel;
using MnemoApp.Data.Runtime;
using MnemoApp.Core.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Modules.Paths.UnitView;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using System.ComponentModel;
using System.Linq;
using System;

namespace MnemoApp.Modules.Paths.UnitOverview;

public class UnitOverviewViewModel : ViewModelBase, IDisposable
{
    private readonly IRuntimeStorage _storage;
    private readonly IMnemoAPI _mnemoAPI;
    private string _pathId;
    private string _pathTitle = string.Empty;
    private Timer? _refreshTimer;
    private bool _disposed;
    
    public string PathTitle
    {
        get => _pathTitle;
        set => SetProperty(ref _pathTitle, value);
    }
    
    public ObservableCollection<UnitInfo> Units { get; } = new();
    public ICommand NavigateToUnitCommand { get; }
    public ICommand GenerateAllUnitsCommand { get; }
    
    public UnitOverviewViewModel(IRuntimeStorage storage, IMnemoAPI mnemoAPI, string pathId)
    {
        _storage = storage;
        _mnemoAPI = mnemoAPI;
        _pathId = pathId;
        
        NavigateToUnitCommand = new RelayCommand<UnitInfo>(NavigateToUnit);
        GenerateAllUnitsCommand = new RelayCommand(GenerateAllUnits);
        
        LoadPathAndUnits();
        StartAutoRefresh();
    }
    
    private void NavigateToUnit(UnitInfo? unitInfo)
    {
        if (unitInfo != null && unitInfo.IsEnabled)
        {
            var unitViewVm = new UnitViewViewModel(_storage, _mnemoAPI, _pathId, unitInfo.Order);
            _mnemoAPI.navigate.Navigate(unitViewVm, "Unit View");
        }
    }

    private void GenerateAllUnits()
    {
        var pathKey = $"Content/Paths/{_pathId}";
        var pathData = _storage.GetProperty<PathData>(pathKey);

        if (pathData?.Units == null) return;

        // Schedule generation for all units without content
        foreach (var unit in pathData.Units)
        {
            if (string.IsNullOrWhiteSpace(unit.Content))
            {
                System.Diagnostics.Debug.WriteLine($"[UNIT_OVERVIEW] Scheduling generation for unit {unit.Order}");
                _mnemoAPI.tasks.scheduleGenerateUnit(_pathId, unit.Order);
            }
        }

        _mnemoAPI.ui.toast.show("Unit Generation", $"Generating all remaining units for {pathData.Title}");
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

    private void StartAutoRefresh()
    {
        // Refresh every 2 seconds to update unit status
        _refreshTimer = new Timer(_ =>
        {
            Dispatcher.UIThread.Post(() => RefreshUnitStatus());
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void RefreshUnitStatus()
    {
        var pathKey = $"Content/Paths/{_pathId}";
        var pathData = _storage.GetProperty<PathData>(pathKey);
        
        if (pathData?.Units == null) return;

        // Update existing unit info
        foreach (var unitInfo in Units)
        {
            var unit = pathData.Units.FirstOrDefault(u => u.Order == unitInfo.Order);
            if (unit != null)
            {
                var hasContent = !string.IsNullOrWhiteSpace(unit.Content);
                if (unitInfo.HasContent != hasContent)
                {
                    unitInfo.HasContent = hasContent;
                    unitInfo.IsEnabled = hasContent;
                }
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _refreshTimer?.Dispose();
            _disposed = true;
        }
    }
}

public class UnitInfo : INotifyPropertyChanged
{
    private bool _hasContent;
    private bool _isEnabled;

    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    
    public bool HasContent
    {
        get => _hasContent;
        set
        {
            if (_hasContent != value)
            {
                _hasContent = value;
                OnPropertyChanged(nameof(HasContent));
            }
        }
    }
    
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

