using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.History;
using Mnemo.Core.Models.Mindmap;
using Mnemo.Core.Services;
using LayoutAlgorithms = Mnemo.Core.Models.Mindmap.LayoutAlgorithms;
using Mnemo.UI.ViewModels;
using MindmapModel = Mnemo.Core.Models.Mindmap.Mindmap;

namespace Mnemo.UI.Modules.Mindmap.ViewModels;

public partial class MindmapViewModel : ViewModelBase, INavigationAware
{
    private readonly IMindmapService _mindmapService;
    private readonly IHistoryManager _historyManager;
    private MindmapModel? _currentMindmap;

    [ObservableProperty]
    private string _title = "Mindmap";

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<EdgeViewModel> Edges { get; } = new();

    private readonly Dictionary<string, List<EdgeViewModel>> _outgoing = new();
    private readonly Dictionary<string, List<EdgeViewModel>> _incoming = new();

    public ICommand AddNodeCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ConnectSelectedCommand { get; }
    public ICommand DetachSelectedCommand { get; }
    public ICommand AutoLayoutCommand { get; }
    public ICommand RecenterCommand { get; }
    public ICommand SetSelectedNodesColorCommand { get; }
    public ICommand SetSelectedNodesShapeCommand { get; }

    /// <summary>First selected node, for properties panel. Refreshes when selection changes.</summary>
    public NodeViewModel? FirstSelectedNode => Nodes.FirstOrDefault(n => n.IsSelected);

    public bool HasSelectedNodes => Nodes.Any(n => n.IsSelected);

    /// <summary>Available layout algorithm IDs for binding.</summary>
    public static IReadOnlyList<string> LayoutAlgorithmIds { get; } = new[] { LayoutAlgorithms.TreeVertical, LayoutAlgorithms.TreeHorizontal, LayoutAlgorithms.Radial };

    [ObservableProperty]
    private string _selectedLayoutAlgorithm = LayoutAlgorithms.TreeVertical;

    public event EventHandler? RecenterRequested;

    public MindmapViewModel(IMindmapService mindmapService, IHistoryManager historyManager)
    {
        _mindmapService = mindmapService;
        _historyManager = historyManager;
        AddNodeCommand = new AsyncRelayCommand(AddNodeAsync);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
        ConnectSelectedCommand = new AsyncRelayCommand(ConnectSelectedAsync);
        DetachSelectedCommand = new AsyncRelayCommand(DetachSelectedAsync);
        AutoLayoutCommand = new AsyncRelayCommand(AutoLayoutAsync);
        RecenterCommand = new RelayCommand(RecenterView);
        SetSelectedNodesColorCommand = new RelayCommand<string?>(SetSelectedNodesColor);
        SetSelectedNodesShapeCommand = new RelayCommand<string?>(SetSelectedNodesShape);
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(FirstSelectedNode));
            OnPropertyChanged(nameof(HasSelectedNodes));
        }
    }

    private async void SetSelectedNodesColor(string? color)
    {
        if (_currentMindmap == null) return;
        var selected = Nodes.Where(n => n.IsSelected).ToList();
        foreach (var node in selected)
        {
            node.Color = color;
            SyncNodeStyleToModel(node);
            await _mindmapService.UpdateNodeStyleAsync(_currentMindmap.Id, node.Id, BuildStyleDict(node));
        }
    }

    private async void SetSelectedNodesShape(string? shape)
    {
        if (_currentMindmap == null || string.IsNullOrEmpty(shape)) return;
        var selected = Nodes.Where(n => n.IsSelected).ToList();
        foreach (var node in selected)
        {
            node.Shape = shape;
            // When switching from circle, clear stored size so the view re-measures (circle forces square; pill/rect use content size).
            if (shape != "circle")
            {
                node.Width = null;
                node.Height = null;
            }
            SyncNodeStyleToModel(node);
            await _mindmapService.UpdateNodeStyleAsync(_currentMindmap.Id, node.Id, BuildStyleDict(node));
        }
    }

    private static Dictionary<string, string?> BuildStyleDict(NodeViewModel node)
    {
        var d = new Dictionary<string, string?>();
        d["color"] = node.Color;
        d["shape"] = node.Shape;
        return d;
    }

    private void SyncNodeStyleToModel(NodeViewModel nodeVm)
    {
        var node = _currentMindmap?.Nodes.FirstOrDefault(n => n.Id == nodeVm.Id);
        if (node == null) return;
        if (nodeVm.Color != null) node.Style["color"] = nodeVm.Color;
        else node.Style.Remove("color");
        node.Style["shape"] = nodeVm.Shape;
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is string id)
        {
            _ = LoadMindmapAsync(id);
        }
        else
        {
            _ = LoadInitialMindmapAsync();
        }
    }

    private async Task LoadInitialMindmapAsync()
    {
        IsLoading = true;
        try
        {
            var mindmapsResult = await _mindmapService.GetAllMindmapsAsync();
            if (mindmapsResult.IsSuccess && mindmapsResult.Value != null && mindmapsResult.Value.Any())
            {
                await LoadMindmapAsync(mindmapsResult.Value.First().Id);
            }
            else
            {
                var createResult = await _mindmapService.CreateMindmapAsync("My First Mindmap");
                if (createResult.IsSuccess && createResult.Value != null)
                {
                    await LoadMindmapAsync(createResult.Value.Id);
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadMindmapAsync(string id)
    {
        var result = await _mindmapService.GetMindmapAsync(id);
        if (result.IsSuccess && result.Value != null)
        {
            _currentMindmap = result.Value;
            Title = _currentMindmap.Title;
            _historyManager.Clear();
            RefreshView();
        }
    }

    partial void OnSelectedLayoutAlgorithmChanged(string value)
    {
        if (_currentMindmap == null || string.IsNullOrEmpty(value)) return;
        if (_currentMindmap.Layout.Algorithm == value) return; // avoid redundant save when syncing from load
        _currentMindmap.Layout.Algorithm = value;
        _ = _mindmapService.UpdateLayoutAlgorithmAsync(_currentMindmap.Id, value);
    }

    private void RefreshView()
    {
        if (_currentMindmap == null) return;

        var algorithm = _currentMindmap.Layout.Algorithm;
        if (string.IsNullOrEmpty(algorithm) || algorithm == "Freeform" || !LayoutAlgorithmIds.Contains(algorithm))
        {
            algorithm = LayoutAlgorithms.TreeVertical;
            _currentMindmap.Layout.Algorithm = algorithm;
        }
        SelectedLayoutAlgorithm = algorithm;
        foreach (var edge in Edges) edge.Dispose();
        Nodes.Clear();
        Edges.Clear();
        _outgoing.Clear();
        _incoming.Clear();

        var nodeMap = new Dictionary<string, NodeViewModel>();

        foreach (var node in _currentMindmap.Nodes)
        {
            if (_currentMindmap.Layout.Nodes.TryGetValue(node.Id, out var layout))
            {
                var nodeVm = new NodeViewModel(node, layout);
                nodeVm.PropertyChanged += OnNodePropertyChanged;
                Nodes.Add(nodeVm);
                nodeMap[node.Id] = nodeVm;
            }
        }

        foreach (var edge in _currentMindmap.Edges)
        {
            if (nodeMap.TryGetValue(edge.FromId, out var from) && nodeMap.TryGetValue(edge.ToId, out var to))
            {
                var edgeVm = new EdgeViewModel(edge, from, to);
                Edges.Add(edgeVm);
                AddToAdjacency(edgeVm);
            }
        }
    }

    private void AddToAdjacency(EdgeViewModel edge)
    {
        if (!_outgoing.ContainsKey(edge.From.Id)) _outgoing[edge.From.Id] = new List<EdgeViewModel>();
        if (!_incoming.ContainsKey(edge.To.Id)) _incoming[edge.To.Id] = new List<EdgeViewModel>();

        _outgoing[edge.From.Id].Add(edge);
        _incoming[edge.To.Id].Add(edge);
    }

    private async Task AddNodeAsync()
    {
        if (_currentMindmap == null) return;

        var selectedNodes = Nodes.Where(n => n.IsSelected).ToList();
        
        double x = 400, y = 300;
        if (selectedNodes.Any())
        {
            var last = selectedNodes.Last();
            x = last.X + 200;
            y = last.Y;
        }

        var result = await _mindmapService.AddNodeAsync(
            _currentMindmap.Id, 
            "text", 
            new TextNodeContent { Text = "New Node" }, 
            x, y);

        if (result.IsSuccess)
        {
            var newNode = result.Value!;
            foreach (var selected in selectedNodes)
            {
                await _mindmapService.AddEdgeAsync(_currentMindmap.Id, selected.Id, newNode.Id, MindmapEdgeKind.Hierarchy);
            }
            
            await LoadMindmapAsync(_currentMindmap.Id);
        }
    }

    private async Task ConnectSelectedAsync()
    {
        if (_currentMindmap == null) return;
        var selected = Nodes.Where(n => n.IsSelected).ToList();
        if (selected.Count < 2) return;

        // Connect first selected to all others
        var from = selected[0];
        bool changed = false;
        for (int i = 1; i < selected.Count; i++)
        {
            var to = selected[i];
            // Check if already connected to avoid duplicates
            if (!_currentMindmap.Edges.Any(e => (e.FromId == from.Id && e.ToId == to.Id) || (e.FromId == to.Id && e.ToId == from.Id)))
            {
                await _mindmapService.AddEdgeAsync(_currentMindmap.Id, from.Id, to.Id, MindmapEdgeKind.Hierarchy);
                changed = true;
            }
        }
        
        if (changed)
        {
            await LoadMindmapAsync(_currentMindmap.Id);
        }
    }

    private async Task DetachSelectedAsync()
    {
        if (_currentMindmap == null) return;
        var selectedIds = Nodes.Where(n => n.IsSelected).Select(n => n.Id).ToHashSet();
        
        // If only one node is selected, we might want to detach all its edges?
        // But the user asked to select two nodes and click it to detach them.
        // Let's support both: if 2+ selected, detach between them. If 1 selected, do nothing (or we could detach all).
        if (selectedIds.Count < 1) return;

        List<MindmapEdge> edgesToRemove;
        if (selectedIds.Count == 1)
        {
            // If only one node selected, maybe detach all its connections?
            // Actually, let's stick to the "select two nodes" requirement first but be more lenient.
            var singleId = selectedIds.First();
            edgesToRemove = _currentMindmap.Edges
                .Where(e => e.FromId == singleId || e.ToId == singleId)
                .ToList();
        }
        else
        {
            // Detach only between selected nodes
            edgesToRemove = _currentMindmap.Edges
                .Where(e => selectedIds.Contains(e.FromId) && selectedIds.Contains(e.ToId))
                .ToList();
        }

        if (!edgesToRemove.Any()) return;

        bool changed = false;
        foreach (var edge in edgesToRemove)
        {
            var result = await _mindmapService.RemoveEdgeAsync(_currentMindmap.Id, edge.Id);
            if (result.IsSuccess) changed = true;
        }

        if (changed)
        {
            await LoadMindmapAsync(_currentMindmap.Id);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_currentMindmap == null) return;

        var selectedNodes = Nodes.Where(n => n.IsSelected).ToList();
        foreach (var node in selectedNodes)
        {
            await _mindmapService.RemoveNodeAsync(_currentMindmap.Id, node.Id);
        }

        if (selectedNodes.Any())
        {
            await LoadMindmapAsync(_currentMindmap.Id);
        }
    }

    public async Task UpdateNodeTextAsync(NodeViewModel node, string text)
    {
        if (_currentMindmap == null) return;
        node.Text = text;
        await _mindmapService.UpdateNodeContentAsync(_currentMindmap.Id, node.Id, new TextNodeContent { Text = text });
    }

    public async Task UpdateNodePositionAsync(NodeViewModel node, double x, double y)
    {
        if (_currentMindmap == null) return;
        node.X = x;
        node.Y = y;
        await _mindmapService.UpdateNodeLayoutAsync(_currentMindmap.Id, node.Id, x, y);
    }

    private async Task AutoLayoutAsync()
    {
        if (_currentMindmap == null || !Nodes.Any()) return;

        var algorithm = _currentMindmap.Layout.Algorithm ?? LayoutAlgorithms.TreeVertical;

        var hierarchyOutgoing = GetHierarchyChildren();
        var roots = Nodes.Where(n => !_currentMindmap!.Edges.Any(e => e.ToId == n.Id && e.Kind == MindmapEdgeKind.Hierarchy)).ToList();
        if (!roots.Any()) roots.Add(Nodes.First());

        var visited = new HashSet<string>();

        switch (algorithm)
        {
            case LayoutAlgorithms.TreeVertical:
                double currentY = 100;
                foreach (var root in roots)
                {
                    currentY = LayoutTreeVertical(root, 100, currentY, hierarchyOutgoing, visited);
                    currentY += 100;
                }
                break;
            case LayoutAlgorithms.TreeHorizontal:
                double currentX = 100;
                foreach (var root in roots)
                {
                    currentX = LayoutTreeHorizontal(root, currentX, 100, hierarchyOutgoing, visited);
                    currentX += 100;
                }
                break;
            case LayoutAlgorithms.Radial:
                const double cx = 400, cy = 300, radiusStep = 180;
                foreach (var root in roots)
                    LayoutRadial(root, cx, cy, 0, radiusStep, 0, Math.Tau, hierarchyOutgoing, visited);
                break;
        }

        foreach (var node in Nodes)
            await _mindmapService.UpdateNodeLayoutAsync(_currentMindmap.Id, node.Id, node.X, node.Y);
    }

    private static Dictionary<string, List<NodeViewModel>> GetHierarchyChildrenFromMindmap(MindmapModel mindmap, IList<NodeViewModel> nodes)
    {
        var nodeById = nodes.ToDictionary(n => n.Id);
        var children = new Dictionary<string, List<NodeViewModel>>();
        foreach (var e in mindmap.Edges.Where(x => x.Kind == MindmapEdgeKind.Hierarchy))
        {
            if (!nodeById.TryGetValue(e.FromId, out var from) || !nodeById.TryGetValue(e.ToId, out var to)) continue;
            if (!children.ContainsKey(from.Id)) children[from.Id] = new List<NodeViewModel>();
            children[from.Id].Add(to);
        }
        return children;
    }

    private Dictionary<string, List<NodeViewModel>> GetHierarchyChildren()
    {
        return GetHierarchyChildrenFromMindmap(_currentMindmap!, Nodes);
    }

    private static double LayoutTreeVertical(NodeViewModel node, double x, double y, IReadOnlyDictionary<string, List<NodeViewModel>> children, HashSet<string> visited)
    {
        if (visited.Contains(node.Id)) return y;
        visited.Add(node.Id);
        node.X = x;
        node.Y = y;
        if (!children.TryGetValue(node.Id, out var childList) || childList.Count == 0)
            return y + 80;
        double childX = x + 250;
        double childY = y;
        double firstChildY = y;
        foreach (var child in childList)
            childY = LayoutTreeVertical(child, childX, childY, children, visited);
        double lastChildBottom = childY - 80;
        node.Y = (firstChildY + lastChildBottom) / 2;
        return childY;
    }

    private static double LayoutTreeHorizontal(NodeViewModel node, double x, double y, IReadOnlyDictionary<string, List<NodeViewModel>> children, HashSet<string> visited)
    {
        if (visited.Contains(node.Id)) return x;
        visited.Add(node.Id);
        node.X = x;
        node.Y = y;
        if (!children.TryGetValue(node.Id, out var childList) || childList.Count == 0)
            return x + 120;
        double childY = y + 200;
        double childX = x;
        double firstChildX = x;
        foreach (var child in childList)
            childX = LayoutTreeHorizontal(child, childX, childY, children, visited);
        double midX = (firstChildX + (childX - 120)) / 2;
        node.X = midX;
        return childX;
    }

    private static void LayoutRadial(NodeViewModel node, double cx, double cy, int level, double radiusStep, double angleStart, double angleEnd, IReadOnlyDictionary<string, List<NodeViewModel>> children, HashSet<string> visited)
    {
        if (visited.Contains(node.Id)) return;
        visited.Add(node.Id);
        double r = level * radiusStep;
        double angle = (angleStart + angleEnd) / 2;
        node.X = cx + r * Math.Cos(angle);
        node.Y = cy + r * Math.Sin(angle);
        if (!children.TryGetValue(node.Id, out var childList) || childList.Count == 0) return;
        double slice = (angleEnd - angleStart) / childList.Count;
        for (int i = 0; i < childList.Count; i++)
        {
            double a0 = angleStart + i * slice;
            double a1 = angleStart + (i + 1) * slice;
            LayoutRadial(childList[i], cx, cy, level + 1, radiusStep, a0, a1, children, visited);
        }
    }

    private void RecenterView()
    {
        RecenterRequested?.Invoke(this, EventArgs.Empty);
    }
}
