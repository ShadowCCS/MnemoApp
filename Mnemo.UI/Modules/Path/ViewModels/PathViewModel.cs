using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;
using Mnemo.UI.Modules.Path.ViewModels;
using Mnemo.UI.Modules.Path.Tasks;

namespace Mnemo.UI.Modules.Path.ViewModels;

public class PathViewModel : ViewModelBase
{
    private readonly IAITaskManager _taskManager;
    private readonly IAIOrchestrator _orchestrator;
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

    public PathViewModel(IAITaskManager taskManager, IAIOrchestrator orchestrator, ILoggerService logger)
    {
        _taskManager = taskManager;
        _orchestrator = orchestrator;
        _logger = logger;

        ToggleViewCommand = new RelayCommand(() => IsGridView = !IsGridView);
        CreateCommand = new RelayCommand(CreateNewItem);

        LoadSampleData();
    }

    private async void CreateNewItem()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;

        var task = new GeneratePathTask(SearchText, _orchestrator, _logger);
        await _taskManager.QueueTaskAsync(task);
        
        // In a real app, we'd navigate to the task view or show a toast
        _logger.Info("Path", $"Started generation for: {SearchText}");
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
