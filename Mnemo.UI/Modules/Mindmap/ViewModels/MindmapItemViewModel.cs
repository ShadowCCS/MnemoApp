using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Mindmap.ViewModels;

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

    public ObservableCollection<NodePreviewViewModel> NodePreviews { get; } = new();
    public ObservableCollection<EdgePreviewViewModel> EdgePreviews { get; } = new();

    public string NodeStats => $"{NodeCount} nodes";
    public string EdgeStats => $"{EdgeCount} edges";
}
