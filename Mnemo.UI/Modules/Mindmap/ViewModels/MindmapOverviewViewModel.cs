using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Mindmap;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;
using Mnemo.UI.Components;

using Mnemo.UI.Components.Overlays;

namespace Mnemo.UI.Modules.Mindmap.ViewModels;

public partial class MindmapOverviewViewModel : ViewModelBase
{
    private readonly IMindmapService _mindmapService;
    private readonly INavigationService _navigation;
    private readonly IOverlayService _overlay;
    private readonly ILoggerService _logger;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isGridView = false;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<MindmapItemViewModel> FrequentlyUsedItems { get; } = new();
    public ObservableCollection<MindmapItemViewModel> AllItems { get; } = new();

    public ICommand ToggleViewCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand OpenMindmapCommand { get; }
    public ICommand DeleteMindmapCommand { get; }

    public MindmapOverviewViewModel(
        IMindmapService mindmapService,
        INavigationService navigation,
        IOverlayService overlay,
        ILoggerService logger)
    {
        _mindmapService = mindmapService;
        _navigation = navigation;
        _overlay = overlay;
        _logger = logger;

        ToggleViewCommand = new RelayCommand(() => IsGridView = !IsGridView);
        CreateCommand = new RelayCommand(CreateNewMindmap);
        OpenMindmapCommand = new RelayCommand<MindmapItemViewModel>(OpenMindmap);
        DeleteMindmapCommand = new AsyncRelayCommand<MindmapItemViewModel>(DeleteMindmapAsync);

        _ = LoadMindmapsAsync();
    }

    private async Task LoadMindmapsAsync()
    {
        IsLoading = true;
        try
        {
            var result = await _mindmapService.GetAllMindmapsAsync();
            if (result.IsSuccess && result.Value != null)
            {
                var mindmaps = result.Value.ToList();
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AllItems.Clear();
                    FrequentlyUsedItems.Clear();

                    foreach (var m in mindmaps)
                    {
                        var vm = new MindmapItemViewModel
                        {
                            Id = m.Id,
                            Name = m.Title,
                            NodeCount = m.Nodes.Count,
                            EdgeCount = m.Edges.Count,
                            LastModified = DateTime.Now.ToString("MM/dd/yyyy"), // Assuming we don't have last modified in model yet
                            PreviewColor = GetRandomColor(m.Id)
                        };
                        AllItems.Add(vm);
                    }

                    foreach (var m in AllItems.Take(4))
                    {
                        FrequentlyUsedItems.Add(m);
                    }
                });
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OpenMindmap(MindmapItemViewModel? item)
    {
        if (item != null)
        {
            _navigation.NavigateTo("mindmap-detail", item.Id);
        }
    }

    private async Task DeleteMindmapAsync(MindmapItemViewModel? item)
    {
        if (item == null) return;

        var result = await _overlay.CreateDialogAsync(
            "Delete Mindmap",
            $"Are you sure you want to delete '{item.Name}'?",
            "Delete",
            "Cancel");

        if (result == "Delete")
        {
            var deleteResult = await _mindmapService.DeleteMindmapAsync(item.Id);
            if (deleteResult.IsSuccess)
            {
                await LoadMindmapsAsync();
                _logger.Info("Mindmap", $"Deleted mindmap: {item.Name}");
            }
            else
            {
                await _overlay.CreateDialogAsync("Error", $"Failed to delete: {deleteResult.ErrorMessage}");
            }
        }
    }

    private void CreateNewMindmap()
    {
        var inputOverlay = new InputDialogOverlay
        {
            Title = "Create New Mindmap",
            Placeholder = "Enter mindmap name...",
            InputValue = "New Mindmap",
            ConfirmText = "Create",
            CancelText = "Cancel"
        };

        var options = new OverlayOptions
        {
            ShowBackdrop = true,
            CloseOnOutsideClick = true
        };

        var id = _overlay.CreateOverlay(inputOverlay, options);

        inputOverlay.OnResult = async (result) =>
        {
            _overlay.CloseOverlay(id);
            
            if (string.IsNullOrWhiteSpace(result)) return;

            try
            {
                var createResult = await _mindmapService.CreateMindmapAsync(result);
                if (createResult.IsSuccess && createResult.Value != null)
                {
                    _navigation.NavigateTo("mindmap-detail", createResult.Value.Id);
                }
                else
                {
                    await _overlay.CreateDialogAsync("Error", $"Failed to create: {createResult.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Mindmap", "Failed to create mindmap", ex);
            }
        };
    }

    private string GetRandomColor(string seed)
    {
        var colors = new[] { "#7C4DFF", "#FF5252", "#00E676", "#00B0FF", "#FFAB00", "#FF4081" };
        var index = Math.Abs(seed.GetHashCode()) % colors.Length;
        return colors[index];
    }
}

public partial class MindmapItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _lastModified = string.Empty;

    [ObservableProperty]
    private int _nodeCount;

    [ObservableProperty]
    private int _edgeCount;

    [ObservableProperty]
    private string _previewColor = "#7C4DFF"; // Default accent color

    public string Stats => $"{NodeCount} nodes, {EdgeCount} edges";
}
