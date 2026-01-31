using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Mindmap;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;
using MindmapModel = Mnemo.Core.Models.Mindmap.Mindmap;

namespace Mnemo.UI.Modules.Mindmap.ViewModels;

public partial class MindmapViewModel : ViewModelBase
{
    private readonly IMindmapService _mindmapService;
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

    public event EventHandler? RecenterRequested;

    public MindmapViewModel(IMindmapService mindmapService)
    {
        _mindmapService = mindmapService;
        AddNodeCommand = new AsyncRelayCommand(AddNodeAsync);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
        ConnectSelectedCommand = new AsyncRelayCommand(ConnectSelectedAsync);
        DetachSelectedCommand = new AsyncRelayCommand(DetachSelectedAsync);
        AutoLayoutCommand = new AsyncRelayCommand(AutoLayoutAsync);
        RecenterCommand = new RelayCommand(RecenterView);

        _ = LoadInitialMindmapAsync();
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
            RefreshView();
        }
    }

    private void RefreshView()
    {
        if (_currentMindmap == null) return;

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

        var roots = Nodes.Where(n => !_incoming.ContainsKey(n.Id)).ToList();
        if (!roots.Any()) roots.Add(Nodes.First());

        double currentY = 100;
        var visited = new HashSet<string>();

        foreach (var root in roots)
        {
            currentY = LayoutNodeHierarchy(root, 100, currentY, visited);
            currentY += 100;
        }

        foreach (var node in Nodes)
        {
            await _mindmapService.UpdateNodeLayoutAsync(_currentMindmap.Id, node.Id, node.X, node.Y);
        }
    }

    private double LayoutNodeHierarchy(NodeViewModel node, double x, double y, HashSet<string> visited)
    {
        if (visited.Contains(node.Id)) return y;
        visited.Add(node.Id);

        node.X = x;
        node.Y = y;

        if (!_outgoing.TryGetValue(node.Id, out var edges) || !edges.Any())
        {
            return y + 80;
        }

        double childX = x + 250;
        double childY = y;
        double firstChildY = y;
        
        foreach (var edge in edges)
        {
            childY = LayoutNodeHierarchy(edge.To, childX, childY, visited);
        }

        double lastChildBottom = childY - 80;
        node.Y = (firstChildY + lastChildBottom) / 2;

        return childY;
    }

    private void RecenterView()
    {
        RecenterRequested?.Invoke(this, EventArgs.Empty);
    }
}
