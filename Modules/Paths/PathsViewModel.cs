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

namespace MnemoApp.Modules.Paths;

public class PathsViewModel : ViewModelBase, IDisposable
{
    private readonly IMnemoAPI _mnemoAPI;
    private readonly IRuntimeStorage _storage;
    private readonly ITaskSchedulerService _taskScheduler;
    
    public ObservableCollection<PathInfo> Paths { get; } = new();
    
    public PathsViewModel(IMnemoAPI mnemoAPI, IRuntimeStorage storage, ITaskSchedulerService taskScheduler)
    {
        _mnemoAPI = mnemoAPI;
        _storage = storage;
        _taskScheduler = taskScheduler;
        
        CreatePathCommand = new RelayCommand(CreatePath);
        NavigateToPathCommand = new RelayCommand<PathInfo>(NavigateToPath);
        
        // Subscribe to task completion to auto-refresh when a path is created
        _taskScheduler.TaskCompleted += OnTaskCompleted;
        
        // Subscribe to navigation changes to reload when returning to this view
        _mnemoAPI.navigate.ViewModelChanged += OnNavigationChanged;
        
        LoadPaths();
    }

    public ICommand CreatePathCommand { get; }
    public ICommand NavigateToPathCommand { get; }
    
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
            _mnemoAPI.navigate.Navigate(unitOverviewVm);
        }
    }
    
    private void OnTaskCompleted(object? sender, TaskEventArgs e)
    {
        // Reload paths when a CreateLearningPathTask completes
        if (e.Task is CreateLearningPathTask)
        {
            LoadPaths();
        }
    }
    
    private void OnNavigationChanged(ViewModelBase viewModel)
    {
        // Reload paths when navigating back to this view
        if (viewModel == this)
        {
            LoadPaths();
        }
    }
    
    public void LoadPaths()
    {
        Paths.Clear();
        
        var pathIds = _storage.GetProperty<string[]>("Content/Paths/list") ?? System.Array.Empty<string>();
        
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
    
    public void Dispose()
    {
        _taskScheduler.TaskCompleted -= OnTaskCompleted;
        _mnemoAPI.navigate.ViewModelChanged -= OnNavigationChanged;
    }
}

public class PathInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public System.DateTime CreatedAt { get; set; }
    public int UnitCount { get; set; }
}