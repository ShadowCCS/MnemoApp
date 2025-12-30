using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;
using Mnemo.UI.Modules.Path.ViewModels;
using Mnemo.UI.Modules.Path.Tasks;
using Mnemo.UI.Components;

namespace Mnemo.UI.Modules.Path.ViewModels;

public class PathViewModel : ViewModelBase, IDisposable
{
    private readonly IAITaskManager _taskManager;
    private readonly IAIOrchestrator _orchestrator;
    private readonly ILearningPathService _pathService;
    private readonly IKnowledgeService _knowledge;
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly IOverlayService _overlay;
    private readonly ILoggerService _logger;

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    private bool _isGridView = false;
    public bool IsGridView
    {
        get => _isGridView;
        set => SetProperty(ref _isGridView, value);
    }

    public ObservableCollection<PathItemViewModel> FrequentlyUsedItems { get; } = new();
    public ObservableCollection<PathBaseViewModel> AllItems { get; } = new();

    public ICommand ToggleViewCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand OpenPathCommand { get; }
    public ICommand DeletePathCommand { get; }

    public PathViewModel(
        IAITaskManager taskManager, 
        IAIOrchestrator orchestrator, 
        ILearningPathService pathService,
        IKnowledgeService knowledge,
        ISettingsService settings,
        INavigationService navigation,
        IOverlayService overlay,
        ILoggerService logger)
    {
        _taskManager = taskManager;
        _orchestrator = orchestrator;
        _pathService = pathService;
        _knowledge = knowledge;
        _settings = settings;
        _navigation = navigation;
        _overlay = overlay;
        _logger = logger;

        _pathService.PathUpdated += OnPathUpdated;

        ToggleViewCommand = new RelayCommand(() => IsGridView = !IsGridView);
        CreateCommand = new RelayCommand(CreateNewItem);
        OpenPathCommand = new RelayCommand<PathBaseViewModel>(OpenPath);
        DeletePathCommand = new RelayCommand<PathBaseViewModel>(DeletePath);

        _ = LoadPathsAsync();
    }

    private async Task LoadPathsAsync()
    {
        var paths = await _pathService.GetAllPathsAsync();
        var pathList = paths.ToList();
        
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            FrequentlyUsedItems.Clear();
            AllItems.Clear();

            foreach (var path in pathList)
            {
                var vm = new PathItemViewModel 
                { 
                    Id = path.PathId,
                    Name = path.Title, 
                    LastModified = path.Metadata.CreatedAt.ToString("MM/dd/yyyy"), 
                    Size = 0,
                    Progress = path.Progress, 
                    Category = path.Difficulty.ToUpper() 
                };
                AllItems.Add(vm);
            }

            foreach (var path in pathList.Take(4))
            {
                var vm = new PathItemViewModel 
                { 
                    Id = path.PathId,
                    Name = path.Title, 
                    LastModified = path.Metadata.CreatedAt.ToString("MM/dd/yyyy"), 
                    Size = 0,
                    Progress = path.Progress, 
                    Category = path.Difficulty.ToUpper() 
                };
                FrequentlyUsedItems.Add(vm);
            }

            if (!AllItems.Any())
            {
                LoadSampleData();
            }
        });
    }

    private void OnPathUpdated(LearningPath path)
    {
        _ = LoadPathsAsync();
    }

    public void Dispose()
    {
        _pathService.PathUpdated -= OnPathUpdated;
    }

    private void OpenPath(PathBaseViewModel? item)
    {
        if (item is PathItemViewModel pathItem && !string.IsNullOrEmpty(pathItem.Id))
        {
            _navigation.NavigateTo("path-detail", pathItem.Id);
        }
    }

    private async void DeletePath(PathBaseViewModel? item)
    {
        if (item is PathItemViewModel pathItem && !string.IsNullOrEmpty(pathItem.Id))
        {
            var result = await _overlay.CreateDialogAsync(
                "Delete Path", 
                $"Are you sure you want to delete '{pathItem.Name}'? This action cannot be undone.",
                "Delete",
                "Cancel");
            
            if (result == "Delete")
            {
                var deleteResult = await _pathService.DeletePathAsync(pathItem.Id);
                if (deleteResult.IsSuccess)
                {
                    _ = LoadPathsAsync();
                    _logger.Info("Path", $"Deleted path: {pathItem.Name}");
                }
                else
                {
                    await _overlay.CreateDialogAsync("Error", $"Failed to delete path: {deleteResult.ErrorMessage}");
                    _logger.Error("Path", $"Failed to delete path: {deleteResult.ErrorMessage}");
                }
            }
        }
    }

    private void CreateNewItem()
    {
        var inputBuilder = new InputBuilder();
        var options = new OverlayOptions
        {
            ShowBackdrop = true,
            CloseOnOutsideClick = true
        };
        
        var id = _overlay.CreateOverlay(inputBuilder, options, "Create Learning Path");
        
        inputBuilder.GenerateRequested += async (s, e) =>
        {
            try 
            {
                _overlay.CloseOverlay(id);
                await StartGeneration(e.text, e.files);
            }
            catch (Exception ex)
            {
                _logger.Error("Path", "Failed to start generation", ex);
                await _overlay.CreateDialogAsync("Error", $"Could not start generation: {ex.Message}");
            }
        };
    }

    private async Task StartGeneration(string topic, string[] files)
    {
        var task = new GeneratePathTask(
            topic, 
            "", 
            files, 
            _orchestrator, 
            _knowledge, 
            _pathService, 
            _settings, 
            _logger);

        await _taskManager.QueueTaskAsync(task);
        
        _logger.Info("Path", $"Started generation for: {topic}");
        
        // Wait for the path to be created in the first step
        while (task.GeneratedPath == null && 
               (task.Status == AITaskStatus.Running || task.Status == AITaskStatus.Pending))
        {
            await Task.Delay(500);
        }

        if (task.Status == AITaskStatus.Failed)
        {
            _logger.Error("Path", $"Generation failed: {task.Steps.FirstOrDefault(s => s.Status == AITaskStatus.Failed)?.ErrorMessage}");
            await _overlay.CreateDialogAsync("Generation Failed", "Could not create the learning path. Please check your connection or try again.");
            return;
        }

        if (task.GeneratedPath != null)
        {
            _navigation.NavigateTo("path-detail", task.GeneratedPath.PathId);
            _ = LoadPathsAsync();
        }
    }

    private void LoadSampleData()
    {
        FrequentlyUsedItems.Add(new PathItemViewModel { Name = "Introduction to Psychology", LastModified = "05/19/2025", Size = 3.2, Progress = 75, Category = "SOCIAL SCIENCE" });
        FrequentlyUsedItems.Add(new PathItemViewModel { Name = "Web Dev Fundamentals", LastModified = "07/03/2025", Size = 2.3, Progress = 45, Category = "DEVELOPMENT" });
        FrequentlyUsedItems.Add(new PathItemViewModel { Name = "The Cell Explained", LastModified = "05/18/2025", Size = 1.5, Progress = 90, Category = "BIOLOGY" });
        FrequentlyUsedItems.Add(new PathItemViewModel { Name = "Industrial Revolution", LastModified = "05/19/2025", Size = 3.4, Progress = 20, Category = "HISTORY" });

        AllItems.Add(new PathItemViewModel { Name = "Introduction to Psychology", LastModified = "05/19/2025", Size = 3.2, Progress = 75, Category = "SOCIAL SCIENCE" });
        AllItems.Add(new PathItemViewModel { Name = "Web Dev Fundamentals", LastModified = "07/03/2025", Size = 2.3, Progress = 45, Category = "DEVELOPMENT" });
        AllItems.Add(new PathItemViewModel { Name = "The Cell Explained", LastModified = "05/18/2025", Size = 1.5, Progress = 90, Category = "BIOLOGY" });
        AllItems.Add(new PathItemViewModel { Name = "Industrial Revolution", LastModified = "05/19/2025", Size = 3.4, Progress = 20, Category = "HISTORY" });

        var scienceFolder = new FolderItemViewModel { Name = "Science Class 2025", LastModified = "07/13/2025", Size = 12.5, Progress = 65, Category = "SCIENCE" };
        AllItems.Add(scienceFolder);

        var historyFolder = new FolderItemViewModel { Name = "World History", LastModified = "05/03/2025", Size = 8.1, Progress = 10, Category = "HISTORY" };
        AllItems.Add(historyFolder);
        
        AllItems.Add(new PathItemViewModel { Name = "Quantum Mechanics", LastModified = "08/12/2025", Size = 4.7, Progress = 5, Category = "PHYSICS" });
        AllItems.Add(new PathItemViewModel { Name = "Organic Chemistry", LastModified = "09/01/2025", Size = 2.9, Progress = 35, Category = "CHEMISTRY" });
    }
}
