using MnemoApp.Core.Common;
using System.Windows.Input;
using MnemoApp.Core.MnemoAPI;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using MnemoApp.Data.Runtime;
using MnemoApp.Core.Tasks;
using MnemoApp.Modules.Paths.UnitOverview;
using MnemoApp.Core.Tasks.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MnemoApp.Modules.Paths;

public class PathsViewModel : ViewModelBase, IDisposable
{
    private readonly IMnemoAPI _mnemoAPI;
    private readonly IRuntimeStorage _storage;
    private readonly ITaskSchedulerService _taskScheduler;
    private readonly Timer _loadPathsDebounceTimer;
    
    public ObservableCollection<PathInfo> Paths { get; } = new();
    
    public PathsViewModel(IMnemoAPI mnemoAPI, IRuntimeStorage storage, ITaskSchedulerService taskScheduler)
    {
        _mnemoAPI = mnemoAPI;
        _storage = storage;
        _taskScheduler = taskScheduler;
        
        // Initialize debounce timer with 100ms delay
        _loadPathsDebounceTimer = new Timer(OnLoadPathsDebounced, null, Timeout.Infinite, Timeout.Infinite);
        
        CreatePathCommand = new RelayCommand(CreatePath);
        NavigateToPathCommand = new RelayCommand<PathInfo>(NavigateToPath);
        WipePathsCommand = new RelayCommand(WipePaths);
        
        // Subscribe to task completion to auto-refresh when a path is created
        _taskScheduler.TaskCompleted += OnTaskCompleted;
        
        // Subscribe to navigation changes to reload when returning to this view
        _mnemoAPI.navigate.ViewModelChanged += OnNavigationChanged;
        
        LoadPaths();
    }

    public ICommand CreatePathCommand { get; }
    public ICommand NavigateToPathCommand { get; }
    public ICommand WipePathsCommand { get; }
    
    private void CreatePath()
    {
        var overlay = new Modules.Paths.Overlays.CreatePathOverlay(_mnemoAPI);
        _mnemoAPI.ui.overlay.Show<string?>(overlay, name: "CreatePathOverlay");
    }
    
    private void NavigateToPath(PathInfo? pathInfo)
    {
        if (pathInfo != null)
        {
            var unitOverviewVm = new UnitOverviewViewModel(_storage, _mnemoAPI, pathInfo.Id);
            _mnemoAPI.navigate.Navigate(unitOverviewVm, "Unit Overview", clearBreadcrumbs: false);
        }
    }
    
    private void WipePaths()
    {
        try
        {
            // Get all path IDs
            var pathIds = _storage.GetProperty<string[]>("Content/Paths/list") ?? Array.Empty<string>();
            
            // Remove each path from storage
            foreach (var pathId in pathIds)
            {
                var pathKey = $"Content/Paths/{pathId}";
                _storage.RemoveProperty(pathKey);
            }
            
            // Clear the path list
            _storage.RemoveProperty("Content/Paths/list");
            
            // Clear the UI collection
            Paths.Clear();
            
            _mnemoAPI.ui.toast.show("Success", $"Cleared {pathIds.Length} paths");
        }
        catch (Exception ex)
        {
            _mnemoAPI.ui.toast.show("Error", $"Failed to clear paths: {ex.Message}");
        }
    }
    
    private void OnTaskCompleted(object? sender, TaskEventArgs e)
    {
        // Reload paths when a CreateLearningPathTask completes
        if (e.Task is CreateLearningPathTask)
        {
            LoadPathsDebounced();
        }
    }
    
    private void OnNavigationChanged(ViewModelBase viewModel)
    {
        // Reload paths when navigating back to this view
        if (viewModel == this)
        {
            LoadPathsDebounced();
        }
    }
    
    private void LoadPathsDebounced()
    {
        // Reset the timer to debounce multiple rapid calls
        _loadPathsDebounceTimer.Change(100, Timeout.Infinite);
    }
    
    private void OnLoadPathsDebounced(object? state)
    {
        LoadPaths();
    }
    
    public void LoadPaths()
    {
        var pathIds = _storage.GetProperty<string[]>("Content/Paths/list") ?? System.Array.Empty<string>();
        
        // Create a set of current path IDs for efficient lookup
        var currentPathIds = new HashSet<string>(Paths.Select(p => p.Id));
        var newPathIds = new HashSet<string>(pathIds);
        
        // Only reload if the path list has actually changed
        if (!currentPathIds.SetEquals(newPathIds))
        {
            Paths.Clear();
            
            foreach (var pathId in pathIds)
            {
                var pathKey = $"Content/Paths/{pathId}";
                var pathData = _storage.GetProperty<PathData>(pathKey);
                
                if (pathData != null)
                {
                    Paths.Add(new PathInfo
                    {
                        Id = pathData.Id,
                        Title = pathData.Title,
                        CreatedAt = pathData.CreatedAt,
                        UnitCount = pathData.Units?.Length ?? 0
                    });
                }
            }
        }
    }
    
    public void Dispose()
    {
        _taskScheduler.TaskCompleted -= OnTaskCompleted;
        _mnemoAPI.navigate.ViewModelChanged -= OnNavigationChanged;
        _loadPathsDebounceTimer?.Dispose();
    }
}

public class PathInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public System.DateTime CreatedAt { get; set; }
    public int UnitCount { get; set; }
}