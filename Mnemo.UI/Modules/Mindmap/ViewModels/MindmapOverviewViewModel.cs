using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;
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
        if (IsLoading) return;
        IsLoading = true;
        
        try
        {
            var result = await _mindmapService.GetAllMindmapsAsync();
            if (result.IsSuccess && result.Value != null)
            {
                var mindmaps = result.Value.ToList();
                
                // Process preview data off the UI thread
                var viewModels = mindmaps.Select(m => 
                {
                    var vm = new MindmapItemViewModel
                    {
                        Id = m.Id,
                        Name = m.Title,
                        NodeCount = m.Nodes.Count,
                        EdgeCount = m.Edges.Count,
                        LastModified = DateTime.Now.ToString("MM/dd/yyyy")
                    };

                    // Generate simple preview
                    if (m.Layout?.Nodes != null && m.Layout.Nodes.Count > 0)
                    {
                        var nodes = m.Layout.Nodes.Values.ToList();
                        double minX = nodes.Min(n => n.X);
                        double maxX = nodes.Max(n => n.X);
                        double minY = nodes.Min(n => n.Y);
                        double maxY = nodes.Max(n => n.Y);

                        double width = maxX - minX;
                        double height = maxY - minY;
                        
                        // Scale to fit card preview area
                        double padding = 20;
                        double targetW = 240;
                        double targetH = 120;

                        double scaleX = width > 0 ? (targetW - padding * 2) / width : 1;
                        double scaleY = height > 0 ? (targetH - padding * 2) / height : 1;
                        double scale = Math.Min(scaleX, scaleY);

                        foreach (var nodeEntry in m.Layout.Nodes)
                        {
                            vm.NodePreviews.Add(new NodePreviewViewModel
                            {
                                X = (nodeEntry.Value.X - minX) * scale + padding,
                                Y = (nodeEntry.Value.Y - minY) * scale + padding
                            });
                        }

                        foreach (var edge in m.Edges)
                        {
                            if (m.Layout.Nodes.TryGetValue(edge.FromId, out var source) &&
                                m.Layout.Nodes.TryGetValue(edge.ToId, out var target))
                            {
                                vm.EdgePreviews.Add(new EdgePreviewViewModel
                                {
                                    X1 = (source.X - minX) * scale + padding,
                                    Y1 = (source.Y - minY) * scale + padding,
                                    X2 = (target.X - minX) * scale + padding,
                                    Y2 = (target.Y - minY) * scale + padding
                                });
                            }
                        }
                    }
                    return vm;
                }).ToList();

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AllItems.Clear();
                    FrequentlyUsedItems.Clear();

                    foreach (var vm in viewModels)
                    {
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
}
