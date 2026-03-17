using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Enums;
using Mnemo.Core.History;
using Mnemo.Core.Models.Mindmap;
using Mnemo.Core.Services;
using LayoutAlgorithms = Mnemo.Core.Models.Mindmap.LayoutAlgorithms;
using Mnemo.UI.Modules.Mindmap.Operations;
using Mnemo.UI.ViewModels;
using MindmapModel = Mnemo.Core.Models.Mindmap.Mindmap;

namespace Mnemo.UI.Modules.Mindmap.ViewModels;

public partial class MindmapViewModel : ViewModelBase, INavigationAware
{
    private const double NewNodeXOffset = 200;
    private const double LayoutTreeVerticalHSpacing = 250;
    private const double LayoutTreeVerticalVSpacing = 80;
    private const double LayoutTreeHorizontalVSpacing = 200;
    private const double LayoutTreeHorizontalHSpacing = 120;
    private const double LayoutRadialCenterX = 400;
    private const double LayoutRadialCenterY = 300;
    private const double LayoutRadialRadiusStep = 180;

    private readonly IMindmapService _mindmapService;
    private readonly IHistoryManager _historyManager;
    private readonly ISettingsService? _settingsService;
    private readonly ILoggerService? _logger;
    private MindmapModel? _currentMindmap;

    [ObservableProperty]
    private string _title = "Mindmap";

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Active toolbar tab: Edit, Style, View.</summary>
    [ObservableProperty]
    private string _toolbarCategory = "Edit";

    /// <summary>Mindmap mode: Edit (toolbar visible, editing enabled) or Preview (toolbar hidden, read-only).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(IsPreviewMode))]
    [NotifyPropertyChangedFor(nameof(IsToolbarVisible))]
    [NotifyPropertyChangedFor(nameof(IsEditingEnabled))]
    private string _mindmapMode = "Edit";

    public bool IsEditMode => MindmapMode == "Edit";
    public bool IsPreviewMode => MindmapMode == "Preview";
    public bool IsToolbarVisible => !IsPreviewMode;
    public bool IsEditingEnabled => !IsPreviewMode;

    [ObservableProperty]
    private string? _defaultNodeColor;

    [ObservableProperty]
    private string _defaultNodeShape = "pill";

    [ObservableProperty]
    private MindmapEdgeKind _defaultEdgeKind = MindmapEdgeKind.Hierarchy;

    [ObservableProperty]
    private bool _showEdgeLabels = true;

    private const string MinimapOverridesKey = "Mindmap.MinimapVisibilityOverrides";
    private string _globalMinimapDefault = "Auto";
    private string? _localMinimapOverride;

    /// <summary>Effective minimap mode: local override for this mindmap, or global default from settings.</summary>
    public string MinimapVisibilityMode
    {
        get => _localMinimapOverride ?? _globalMinimapDefault;
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            _localMinimapOverride = value;
            OnPropertyChanged(nameof(MinimapVisibilityMode));
            OnPropertyChanged(nameof(IsMinimapOff));
            OnPropertyChanged(nameof(IsMinimapAuto));
            OnPropertyChanged(nameof(IsMinimapOn));
            if (_currentMindmap != null && _settingsService != null)
                _ = SaveMinimapOverrideAsync(_currentMindmap.Id, value);
        }
    }

    public bool IsMinimapOff { get => MinimapVisibilityMode == "Off"; set { if (value) MinimapVisibilityMode = "Off"; } }
    public bool IsMinimapAuto { get => MinimapVisibilityMode == "Auto"; set { if (value) MinimapVisibilityMode = "Auto"; } }
    public bool IsMinimapOn { get => MinimapVisibilityMode == "On"; set { if (value) MinimapVisibilityMode = "On"; } }

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<EdgeViewModel> Edges { get; } = new();

    private readonly Dictionary<string, List<EdgeViewModel>> _outgoing = new();
    private readonly Dictionary<string, List<EdgeViewModel>> _incoming = new();

    public ICommand AddNodeCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ConnectSelectedCommand { get; }
    public ICommand DetachSelectedCommand { get; }
    public ICommand SetLayoutAlgorithmCommand { get; }
    public ICommand RecenterCommand { get; }
    public ICommand SetSelectedNodesColorCommand { get; }
    public ICommand SetSelectedNodesShapeCommand { get; }
    public ICommand SetSelectedEdgeKindCommand { get; }
    public ICommand SetSelectedEdgeTypeCommand { get; }
    public ICommand SetMinimapVisibilityCommand { get; }
    public ICommand SetToolbarCategoryCommand { get; }
    public ICommand SetMindmapModeCommand { get; }
    /// <summary>Command to request PNG export of the mindmap viewport. View handles capture and save.</summary>
    public ICommand ExportAsPngCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }

    public bool CanUndo => _historyManager.CanUndo;
    public bool CanRedo => _historyManager.CanRedo;

    /// <summary>When true, the view should not auto-recenter on the next Nodes collection change (e.g. undo/redo restore).</summary>
    public bool SuppressRecenterOnNextCollectionChange { get; set; }

    public static IReadOnlyList<string> ToolbarCategories { get; } = new[] { "Edit", "Style", "View" };
    public static IReadOnlyList<string> MinimapVisibilityOptions { get; } = new[] { "Auto", "On", "Off" };
    public static IReadOnlyList<MindmapEdgeKind> EdgeKindOptions { get; } = new[] { MindmapEdgeKind.Hierarchy, MindmapEdgeKind.Link };
    public static IReadOnlyList<string> EdgeTypeIds { get; } = EdgeTypes.All;

    /// <summary>First selected node, for properties panel. Refreshes when selection changes.</summary>
    public NodeViewModel? FirstSelectedNode => Nodes.FirstOrDefault(n => n.IsSelected);

    public bool HasSelectedNodes => Nodes.Any(n => n.IsSelected);

    /// <summary>Color to show in Style tab: selection or default for new nodes.</summary>
    public string? EffectiveStyleColor => HasSelectedNodes ? FirstSelectedNode?.Color : DefaultNodeColor;

    /// <summary>Shape to show in Style tab: selection or default for new nodes.</summary>
    public string EffectiveStyleShape => HasSelectedNodes ? (FirstSelectedNode?.Shape ?? "pill") : DefaultNodeShape;

    /// <summary>Edge kind to show in Style tab: selected edge or default for new edges.</summary>
    public MindmapEdgeKind EffectiveEdgeKind => SelectedEdge != null ? SelectedEdge.Kind : DefaultEdgeKind;

    /// <summary>Settable for ComboBox two-way bind; get returns EffectiveEdgeKind, set applies to selection or default.</summary>
    public MindmapEdgeKind StyleEdgeKindSelected
    {
        get => EffectiveEdgeKind;
        set => SetSelectedEdgeKind(value);
    }

    [ObservableProperty]
    private string _defaultEdgeType = EdgeTypes.Solid;

    /// <summary>Edge type to show in Style tab: selected edge or default for new edges.</summary>
    public string EffectiveEdgeType => SelectedEdge != null ? SelectedEdge.Type : DefaultEdgeType;

    /// <summary>Settable for toolbar; get returns EffectiveEdgeType, set applies to selection or default.</summary>
    public string StyleEdgeTypeSelected
    {
        get => EffectiveEdgeType;
        set => SetSelectedEdgeType(value);
    }

    public bool IsEdgeTypeSolid => EffectiveEdgeType == EdgeTypes.Solid;
    public bool IsEdgeTypeDashed => EffectiveEdgeType == EdgeTypes.Dashed;
    public bool IsEdgeTypeDotted => EffectiveEdgeType == EdgeTypes.Dotted;
    public bool IsEdgeTypeDouble => EffectiveEdgeType == EdgeTypes.Double;
    public bool IsEdgeTypeArrow => EffectiveEdgeType == EdgeTypes.Arrow;
    public bool IsEdgeTypeBidirect => EffectiveEdgeType == EdgeTypes.Bidirect;

    public bool IsEditTab => ToolbarCategory == "Edit";
    public bool IsStyleTab => ToolbarCategory == "Style";
    public bool IsViewTab => ToolbarCategory == "View";

    /// <summary>Available layout algorithm IDs for binding.</summary>
    public static IReadOnlyList<string> LayoutAlgorithmIds { get; } = new[] { LayoutAlgorithms.TreeVertical, LayoutAlgorithms.TreeHorizontal, LayoutAlgorithms.Radial };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLayoutTreeVertical))]
    [NotifyPropertyChangedFor(nameof(IsLayoutTreeHorizontal))]
    [NotifyPropertyChangedFor(nameof(IsLayoutRadial))]
    private string _selectedLayoutAlgorithm = LayoutAlgorithms.TreeVertical;

    public bool IsLayoutTreeVertical => SelectedLayoutAlgorithm == LayoutAlgorithms.TreeVertical;
    public bool IsLayoutTreeHorizontal => SelectedLayoutAlgorithm == LayoutAlgorithms.TreeHorizontal;
    public bool IsLayoutRadial => SelectedLayoutAlgorithm == LayoutAlgorithms.Radial;

    public bool IsShapeRectangle => EffectiveStyleShape == "rectangle";
    public bool IsShapePill => EffectiveStyleShape == "pill";
    public bool IsShapeCircle => EffectiveStyleShape == "circle";

    [ObservableProperty]
    private EdgeViewModel? _selectedEdge;

    private string? _hoveredEdgeId;
    private readonly HashSet<string> _hoveredNodeIds = new();

    /// <summary>When true, PNG export uses a transparent background instead of the workspace color.</summary>
    [ObservableProperty]
    private bool _exportPngTransparentBackground;

    public event EventHandler? RecenterRequested;

    public MindmapViewModel(IMindmapService mindmapService, IHistoryManager historyManager, ISettingsService? settingsService = null, ILoggerService? logger = null)
    {
        _mindmapService = mindmapService;
        _historyManager = historyManager;
        _settingsService = settingsService;
        _logger = logger;
        AddNodeCommand = new AsyncRelayCommand(AddNodeAsync);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
        ConnectSelectedCommand = new AsyncRelayCommand(ConnectSelectedAsync);
        DetachSelectedCommand = new AsyncRelayCommand(DetachSelectedAsync);
        SetLayoutAlgorithmCommand = new AsyncRelayCommand<string?>(SetLayoutAlgorithmAsync);
        RecenterCommand = new RelayCommand(RecenterView);
        SetSelectedNodesColorCommand = new RelayCommand<string?>(SetSelectedNodesColor);
        SetSelectedNodesShapeCommand = new RelayCommand<string?>(SetSelectedNodesShape);
        SetSelectedEdgeKindCommand = new RelayCommand<MindmapEdgeKind?>(SetSelectedEdgeKind);
        SetSelectedEdgeTypeCommand = new RelayCommand<string?>(SetSelectedEdgeType);
        SetMinimapVisibilityCommand = new RelayCommand<string?>(SetMinimapVisibility);
        SetToolbarCategoryCommand = new RelayCommand<string?>(c => { if (!string.IsNullOrEmpty(c)) ToolbarCategory = c; });
        SetMindmapModeCommand = new RelayCommand<string?>(c => { if (!string.IsNullOrEmpty(c)) MindmapMode = c; });
        ExportAsPngCommand = new RelayCommand(OnExportAsPng);
        UndoCommand = new AsyncRelayCommand(UndoAsync, () => CanUndo);
        RedoCommand = new AsyncRelayCommand(RedoAsync, () => CanRedo);
        _historyManager.StateChanged += OnHistoryStateChanged;
    }

    private void OnHistoryStateChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        (UndoCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (RedoCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }

    private async Task RestoreMindmapStateAsync(MindmapModel m)
    {
        _currentMindmap = m;
        Title = m.Title;
        SuppressRecenterOnNextCollectionChange = true;
        RefreshView();
        await _mindmapService.SaveMindmapAsync(m);
    }

    public async Task UndoAsync()
    {
        if (!_historyManager.CanUndo) return;
        await _historyManager.UndoAsync();
    }

    public async Task RedoAsync()
    {
        if (!_historyManager.CanRedo) return;
        await _historyManager.RedoAsync();
    }

    private void SyncLayoutFromView()
    {
        if (_currentMindmap == null) return;
        foreach (var n in Nodes)
        {
            if (!_currentMindmap.Layout.Nodes.TryGetValue(n.Id, out var layout))
            {
                layout = new NodeLayout();
                _currentMindmap.Layout.Nodes[n.Id] = layout;
            }
            layout.X = n.X;
            layout.Y = n.Y;
            layout.Width = n.Width;
            layout.Height = n.Height;
        }
    }

    /// <summary>Raised when user requests PNG export. View captures viewport and saves.</summary>
    public event EventHandler? ExportRequested;

    private void OnExportAsPng() => ExportRequested?.Invoke(this, EventArgs.Empty);

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(FirstSelectedNode));
            OnPropertyChanged(nameof(HasSelectedNodes));
            NotifyEffectiveStyleChanged();
        }
        else if (e.PropertyName is nameof(NodeViewModel.Color) or nameof(NodeViewModel.Shape))
        {
            NotifyEffectiveStyleChanged();
        }
    }

    private void NotifyEffectiveStyleChanged()
    {
        OnPropertyChanged(nameof(EffectiveStyleColor));
        OnPropertyChanged(nameof(EffectiveStyleShape));
        OnPropertyChanged(nameof(IsShapeRectangle));
        OnPropertyChanged(nameof(IsShapePill));
        OnPropertyChanged(nameof(IsShapeCircle));
        NotifyEffectiveEdgeTypeChanged();
    }

    private void NotifyEffectiveEdgeTypeChanged()
    {
        OnPropertyChanged(nameof(EffectiveEdgeType));
        OnPropertyChanged(nameof(StyleEdgeTypeSelected));
        OnPropertyChanged(nameof(IsEdgeTypeSolid));
        OnPropertyChanged(nameof(IsEdgeTypeDashed));
        OnPropertyChanged(nameof(IsEdgeTypeDotted));
        OnPropertyChanged(nameof(IsEdgeTypeDouble));
        OnPropertyChanged(nameof(IsEdgeTypeArrow));
        OnPropertyChanged(nameof(IsEdgeTypeBidirect));
    }

    partial void OnDefaultNodeColorChanged(string? value)
    {
        if (!HasSelectedNodes) NotifyEffectiveStyleChanged();
    }

    partial void OnDefaultNodeShapeChanged(string value)
    {
        if (!HasSelectedNodes) NotifyEffectiveStyleChanged();
    }

    partial void OnDefaultEdgeTypeChanged(string value)
    {
        if (SelectedEdge == null) NotifyEffectiveEdgeTypeChanged();
    }

    partial void OnSelectedEdgeChanged(EdgeViewModel? value)
    {
        OnPropertyChanged(nameof(EffectiveEdgeKind));
        OnPropertyChanged(nameof(StyleEdgeKindSelected));
        NotifyEffectiveEdgeTypeChanged();
    }

    partial void OnDefaultEdgeKindChanged(MindmapEdgeKind value)
    {
        if (SelectedEdge == null)
        {
            OnPropertyChanged(nameof(EffectiveEdgeKind));
            OnPropertyChanged(nameof(StyleEdgeKindSelected));
        }
    }

    partial void OnToolbarCategoryChanged(string value)
    {
        OnPropertyChanged(nameof(IsEditTab));
        OnPropertyChanged(nameof(IsStyleTab));
        OnPropertyChanged(nameof(IsViewTab));
    }

    // IsEditMode/IsPreviewMode/IsToolbarVisible/IsEditingEnabled are already notified
    // by the [NotifyPropertyChangedFor] attributes on _mindmapMode; no manual notifications needed.

    private void SetMinimapVisibility(string? mode)
    {
        if (string.IsNullOrEmpty(mode)) return;
        MinimapVisibilityMode = mode;
    }

    private async Task SaveMinimapOverrideAsync(string mindmapId, string mode)
    {
        if (_settingsService == null) return;
        var overrides = await _settingsService.GetAsync(MinimapOverridesKey, new Dictionary<string, string>()).ConfigureAwait(false)
            ?? new Dictionary<string, string>();
        if (mode == _globalMinimapDefault)
            overrides.Remove(mindmapId);
        else
            overrides[mindmapId] = mode;
        await _settingsService.SetAsync(MinimapOverridesKey, overrides).ConfigureAwait(false);
    }

    /// <summary>Refreshes global default from settings. When global changes, clears all per-mindmap overrides so every mindmap uses the new global until locally changed.</summary>
    public async Task RefreshGlobalMinimapSettingAsync()
    {
        if (_settingsService == null) return;
        var mode = await _settingsService.GetAsync("Mindmap.MinimapVisibility", "Auto").ConfigureAwait(false);
        if (mode == null) return;
        _globalMinimapDefault = mode;
        var overrides = await _settingsService.GetAsync(MinimapOverridesKey, new Dictionary<string, string>()).ConfigureAwait(false)
            ?? new Dictionary<string, string>();
        if (overrides.Count > 0)
        {
            overrides.Clear();
            await _settingsService.SetAsync(MinimapOverridesKey, overrides).ConfigureAwait(false);
        }
        _localMinimapOverride = null;
        OnPropertyChanged(nameof(MinimapVisibilityMode));
        OnPropertyChanged(nameof(IsMinimapOff));
        OnPropertyChanged(nameof(IsMinimapAuto));
        OnPropertyChanged(nameof(IsMinimapOn));
    }

    private async void SetSelectedEdgeKind(MindmapEdgeKind? kind)
    {
        try
        {
            if (kind == null) return;
            if (SelectedEdge != null && _currentMindmap != null)
            {
                var edge = _currentMindmap.Edges.FirstOrDefault(e => e.Id == SelectedEdge.Id);
                if (edge != null && kind == MindmapEdgeKind.Hierarchy && WouldCreateCycle(_currentMindmap, edge.FromId, edge.ToId))
                    return;
                SelectedEdge.Kind = kind.Value;
                if (edge != null)
                {
                    edge.Kind = kind.Value;
                    await _mindmapService.UpdateEdgeKindAsync(_currentMindmap.Id, edge.Id, kind.Value);
                }
                OnPropertyChanged(nameof(EffectiveEdgeKind));
                OnPropertyChanged(nameof(StyleEdgeKindSelected));
            }
            else
            {
                DefaultEdgeKind = kind.Value;
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(nameof(MindmapViewModel), "Failed to set edge kind", ex);
        }
    }

    private async void SetSelectedEdgeType(string? type)
    {
        try
        {
            if (string.IsNullOrEmpty(type) || !EdgeTypeIds.Contains(type)) return;
            if (SelectedEdge != null && _currentMindmap != null)
            {
                var edge = _currentMindmap.Edges.FirstOrDefault(e => e.Id == SelectedEdge.Id);
                if (edge != null)
                {
                    var before = MindmapSnapshotHelper.Clone(_currentMindmap);
                    SelectedEdge.Type = type;
                    edge.Type = type;
                    var after = MindmapSnapshotHelper.Clone(_currentMindmap);
                    _historyManager.Push(new MindmapStateOperation("Change edge type", before, after, RestoreMindmapStateAsync));
                    await _mindmapService.UpdateEdgeTypeAsync(_currentMindmap.Id, edge.Id, type);
                }
                NotifyEffectiveEdgeTypeChanged();
            }
            else
            {
                DefaultEdgeType = type;
                NotifyEffectiveEdgeTypeChanged();
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(nameof(MindmapViewModel), "Failed to set edge type", ex);
        }
    }

    private bool WouldCreateCycle(MindmapModel mindmap, string fromId, string toId)
    {
        return _mindmapService.WouldCreateCycle(mindmap, fromId, toId);
    }

    public async void EdgeClicked(EdgeViewModel edge)
    {
        try
        {
            if (_currentMindmap == null) return;
            if (edge.Label == null)
            {
                var modelEdge = _currentMindmap.Edges.FirstOrDefault(e => e.Id == edge.Id);
                if (modelEdge != null)
                {
                    var before = MindmapSnapshotHelper.Clone(_currentMindmap);
                    modelEdge.Label = "";
                    edge.Label = "";
                    var after = MindmapSnapshotHelper.Clone(_currentMindmap);
                    _historyManager.Push(new MindmapStateOperation("Add edge label", before, after, RestoreMindmapStateAsync));
                    await _mindmapService.UpdateEdgeLabelAsync(_currentMindmap.Id, edge.Id, "").ConfigureAwait(false);
                }
            }
            SelectedEdge = edge;
        }
        catch (Exception ex)
        {
            _logger?.Error(nameof(MindmapViewModel), "Failed to process edge click", ex);
        }
    }

    public async void CommitEdgeLabel(EdgeViewModel edge)
    {
        try
        {
            if (_currentMindmap == null) return;
            var mindmapId = _currentMindmap.Id;
            var edgeId = edge.Id;
            var labelToSave = edge.Label;
            var modelEdge = _currentMindmap.Edges.FirstOrDefault(e => e.Id == edgeId);
            if (modelEdge == null) return;

            var before = MindmapSnapshotHelper.Clone(_currentMindmap);
            if (string.IsNullOrWhiteSpace(labelToSave))
            {
                modelEdge.Label = null;
                edge.Label = null;
            }
            else
            {
                modelEdge.Label = labelToSave;
            }
            var after = MindmapSnapshotHelper.Clone(_currentMindmap);
            _historyManager.Push(new MindmapStateOperation("Edit edge label", before, after, RestoreMindmapStateAsync));

            if (string.IsNullOrWhiteSpace(labelToSave))
                await _mindmapService.UpdateEdgeLabelAsync(mindmapId, edgeId, null).ConfigureAwait(false);
            else
                await _mindmapService.UpdateEdgeLabelAsync(mindmapId, edgeId, labelToSave).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.Error(nameof(MindmapViewModel), "Failed to commit edge label", ex);
        }
    }

    public void SetHoveredEdge(string? edgeId)
    {
        if (_hoveredEdgeId == edgeId) return;
        var previousEdgeId = _hoveredEdgeId;
        _hoveredEdgeId = edgeId;
        UpdateEdgeHighlightingForEdges(previousEdgeId, edgeId);
    }

    public void SetHoveredNode(string nodeId, bool hovered)
    {
        if (hovered)
            _hoveredNodeIds.Add(nodeId);
        else
            _hoveredNodeIds.Remove(nodeId);
        UpdateEdgeHighlightingForNode(nodeId);
    }

    public void ClearHoverState()
    {
        if (_hoveredEdgeId == null && _hoveredNodeIds.Count == 0) return;
        var prevEdgeId = _hoveredEdgeId;
        var nodeIds = _hoveredNodeIds.ToList();
        _hoveredEdgeId = null;
        _hoveredNodeIds.Clear();
        UpdateEdgeHighlightingForEdges(prevEdgeId, null);
        foreach (var nid in nodeIds) UpdateEdgeHighlightingForNode(nid);
    }

    private void UpdateEdgeHighlightingForEdges(string? edgeId1, string? edgeId2)
    {
        foreach (var id in new[] { edgeId1, edgeId2 }.Where(x => x != null).Distinct())
        {
            var edge = Edges.FirstOrDefault(e => e.Id == id);
            if (edge != null)
                edge.IsLabelHighlighted = edge.Id == _hoveredEdgeId
                    || _hoveredNodeIds.Contains(edge.From.Id)
                    || _hoveredNodeIds.Contains(edge.To.Id);
        }
    }

    private void UpdateEdgeHighlightingForNode(string nodeId)
    {
        var edgesToUpdate = new List<EdgeViewModel>();
        if (_outgoing.TryGetValue(nodeId, out var outEdges)) edgesToUpdate.AddRange(outEdges);
        if (_incoming.TryGetValue(nodeId, out var inEdges)) edgesToUpdate.AddRange(inEdges);
        foreach (var edge in edgesToUpdate.Distinct())
            edge.IsLabelHighlighted = edge.Id == _hoveredEdgeId
                || _hoveredNodeIds.Contains(edge.From.Id)
                || _hoveredNodeIds.Contains(edge.To.Id);
    }

    private async void SetSelectedNodesColor(string? color)
    {
        try
        {
            if (!HasSelectedNodes)
            {
                DefaultNodeColor = color;
                return;
            }
            if (_currentMindmap == null) return;
            var selected = Nodes.Where(n => n.IsSelected).ToList();
            foreach (var node in selected)
            {
                node.Color = color;
                SyncNodeStyleToModel(node);
                await _mindmapService.UpdateNodeStyleAsync(_currentMindmap.Id, node.Id, BuildStyleDict(node));
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(nameof(MindmapViewModel), "Failed to set node color", ex);
        }
    }

    private async void SetSelectedNodesShape(string? shape)
    {
        try
        {
            if (string.IsNullOrEmpty(shape)) return;
            if (!HasSelectedNodes)
            {
                DefaultNodeShape = shape;
                return;
            }
            if (_currentMindmap == null) return;
            var selected = Nodes.Where(n => n.IsSelected).ToList();
            foreach (var node in selected)
            {
                node.Shape = shape;
                // Clear persisted size for all shape changes so the node re-measures with
                // the new shape constraints instead of being stuck at the old fixed size.
                node.Width = null;
                node.Height = null;
                SyncNodeStyleToModel(node);
                await _mindmapService.UpdateNodeStyleAsync(_currentMindmap.Id, node.Id, BuildStyleDict(node));
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(nameof(MindmapViewModel), "Failed to set node shape", ex);
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
        if (_settingsService != null)
            _ = LoadMinimapSettingAsync();
        if (parameter is string id)
        {
            _ = LoadMindmapAsync(id);
        }
        else
        {
            _ = LoadInitialMindmapAsync();
        }
    }

    private async Task LoadMinimapSettingAsync()
    {
        if (_settingsService == null) return;
        var mode = await _settingsService.GetAsync("Mindmap.MinimapVisibility", "Auto").ConfigureAwait(false);
        if (mode != null) _globalMinimapDefault = mode;
        if (_currentMindmap == null && _localMinimapOverride == null)
        {
            OnPropertyChanged(nameof(MinimapVisibilityMode));
            OnPropertyChanged(nameof(IsMinimapOff));
            OnPropertyChanged(nameof(IsMinimapAuto));
            OnPropertyChanged(nameof(IsMinimapOn));
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
            bool isNewMindmap = _currentMindmap?.Id != id;
            _currentMindmap = result.Value;
            Title = _currentMindmap.Title;
            if (isNewMindmap)
                _historyManager.Clear();
            if (_settingsService != null)
            {
                var overrides = await _settingsService.GetAsync(MinimapOverridesKey, new Dictionary<string, string>()).ConfigureAwait(false)
                    ?? new Dictionary<string, string>();
                _localMinimapOverride = overrides.TryGetValue(id, out var saved) ? saved : null;
            }
            else
                _localMinimapOverride = null;
            OnPropertyChanged(nameof(MinimapVisibilityMode));
            OnPropertyChanged(nameof(IsMinimapOff));
            OnPropertyChanged(nameof(IsMinimapAuto));
            OnPropertyChanged(nameof(IsMinimapOn));
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

        var before = MindmapSnapshotHelper.Clone(_currentMindmap);
        var selectedNodes = Nodes.Where(n => n.IsSelected).ToList();
        double x = LayoutRadialCenterX, y = LayoutRadialCenterY;
        if (selectedNodes.Any())
        {
            var last = selectedNodes.Last();
            x = last.X + NewNodeXOffset;
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
            var style = new Dictionary<string, string?>();
            if (DefaultNodeColor != null) style["color"] = DefaultNodeColor;
            style["shape"] = DefaultNodeShape;
            if (style.Count > 0)
                await _mindmapService.UpdateNodeStyleAsync(_currentMindmap.Id, newNode.Id, style);
            foreach (var selected in selectedNodes)
                await _mindmapService.AddEdgeAsync(_currentMindmap.Id, selected.Id, newNode.Id, DefaultEdgeKind, null, DefaultEdgeType);

            await LoadMindmapAsync(_currentMindmap.Id);
            var after = MindmapSnapshotHelper.Clone(_currentMindmap!);
            _historyManager.Push(new MindmapStateOperation("Add node", before, after, RestoreMindmapStateAsync));
        }
    }

    private async Task ConnectSelectedAsync()
    {
        if (_currentMindmap == null) return;
        var selected = Nodes.Where(n => n.IsSelected).ToList();
        if (selected.Count < 2) return;

        var before = MindmapSnapshotHelper.Clone(_currentMindmap);
        var from = selected[0];
        bool changed = false;
        for (int i = 1; i < selected.Count; i++)
        {
            var to = selected[i];
            if (!_currentMindmap.Edges.Any(e => (e.FromId == from.Id && e.ToId == to.Id) || (e.FromId == to.Id && e.ToId == from.Id)))
            {
                await _mindmapService.AddEdgeAsync(_currentMindmap.Id, from.Id, to.Id, DefaultEdgeKind, null, DefaultEdgeType);
                changed = true;
            }
        }

        if (changed)
        {
            await LoadMindmapAsync(_currentMindmap.Id);
            var after = MindmapSnapshotHelper.Clone(_currentMindmap!);
            _historyManager.Push(new MindmapStateOperation("Connect nodes", before, after, RestoreMindmapStateAsync));
        }
    }

    private async Task DetachSelectedAsync()
    {
        if (_currentMindmap == null) return;
        var selectedIds = Nodes.Where(n => n.IsSelected).Select(n => n.Id).ToHashSet();
        if (selectedIds.Count < 1) return;

        List<MindmapEdge> edgesToRemove;
        if (selectedIds.Count == 1)
        {
            var singleId = selectedIds.First();
            edgesToRemove = _currentMindmap.Edges
                .Where(e => e.FromId == singleId || e.ToId == singleId)
                .ToList();
        }
        else
        {
            edgesToRemove = _currentMindmap.Edges
                .Where(e => selectedIds.Contains(e.FromId) && selectedIds.Contains(e.ToId))
                .ToList();
        }

        if (!edgesToRemove.Any()) return;

        var before = MindmapSnapshotHelper.Clone(_currentMindmap);
        bool changed = false;
        foreach (var edge in edgesToRemove)
        {
            var result = await _mindmapService.RemoveEdgeAsync(_currentMindmap.Id, edge.Id);
            if (result.IsSuccess) changed = true;
        }

        if (changed)
        {
            await LoadMindmapAsync(_currentMindmap.Id);
            var after = MindmapSnapshotHelper.Clone(_currentMindmap!);
            _historyManager.Push(new MindmapStateOperation("Detach edges", before, after, RestoreMindmapStateAsync));
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_currentMindmap == null) return;
        var selectedNodes = Nodes.Where(n => n.IsSelected).ToList();
        if (!selectedNodes.Any()) return;

        var before = MindmapSnapshotHelper.Clone(_currentMindmap);
        foreach (var node in selectedNodes)
            await _mindmapService.RemoveNodeAsync(_currentMindmap.Id, node.Id);

        await LoadMindmapAsync(_currentMindmap.Id);
        var after = MindmapSnapshotHelper.Clone(_currentMindmap!);
        _historyManager.Push(new MindmapStateOperation("Delete nodes", before, after, RestoreMindmapStateAsync));
    }

    public async Task UpdateNodeTextAsync(NodeViewModel node, string text)
    {
        if (_currentMindmap == null) return;
        var before = MindmapSnapshotHelper.Clone(_currentMindmap);
        var mn = _currentMindmap.Nodes.FirstOrDefault(n => n.Id == node.Id);
        if (mn != null)
        {
            if (mn.Content is TextNodeContent existingContent)
                existingContent.Text = text;
            else
                mn.Content = new TextNodeContent { Text = text };
        }
        var after = MindmapSnapshotHelper.Clone(_currentMindmap);
        _historyManager.Push(new MindmapStateOperation("Edit node", before, after, RestoreMindmapStateAsync));
        node.Text = text;
        await _mindmapService.UpdateNodeContentAsync(_currentMindmap.Id, node.Id, new TextNodeContent { Text = text });
    }

    /// <summary>Capture current mindmap state for a move operation. Call at drag start so undo restores pre-drag positions.</summary>
    public MindmapModel? CaptureMoveSnapshot() => _currentMindmap == null ? null : MindmapSnapshotHelper.Clone(_currentMindmap);

    /// <summary>Apply one or more node position updates and push a single undo entry. Use the snapshot from CaptureMoveSnapshot() at drag start.</summary>
    public async Task UpdateNodesPositionAsync(MindmapModel before, IReadOnlyList<(NodeViewModel node, double x, double y)> moves)
    {
        if (_currentMindmap == null || moves.Count == 0) return;
        foreach (var (node, x, y) in moves)
        {
            if (!_currentMindmap.Layout.Nodes.TryGetValue(node.Id, out var layout))
            {
                layout = new NodeLayout();
                _currentMindmap.Layout.Nodes[node.Id] = layout;
            }
            layout.X = x;
            layout.Y = y;
            node.X = x;
            node.Y = y;
        }
        var after = MindmapSnapshotHelper.Clone(_currentMindmap);
        _historyManager.Push(new MindmapStateOperation("Move node", before, after, RestoreMindmapStateAsync));
        foreach (var (node, x, y) in moves)
            await _mindmapService.UpdateNodeLayoutAsync(_currentMindmap.Id, node.Id, x, y);
    }

    public async Task UpdateNodePositionAsync(NodeViewModel node, double x, double y)
    {
        if (_currentMindmap == null) return;
        var before = MindmapSnapshotHelper.Clone(_currentMindmap);
        await UpdateNodesPositionAsync(before, new[] { (node, x, y) });
    }

    private async Task SetLayoutAlgorithmAsync(string? algorithmId)
    {
        if (_currentMindmap == null || string.IsNullOrEmpty(algorithmId) || !LayoutAlgorithmIds.Contains(algorithmId)) return;
        SelectedLayoutAlgorithm = algorithmId;
        await ApplyLayoutAsync();
    }

    private async Task ApplyLayoutAsync()
    {
        if (_currentMindmap == null || !Nodes.Any()) return;

        var before = MindmapSnapshotHelper.Clone(_currentMindmap);
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
                foreach (var root in roots)
                    LayoutRadial(root, LayoutRadialCenterX, LayoutRadialCenterY, 0, LayoutRadialRadiusStep, 0, Math.Tau, hierarchyOutgoing, visited);
                break;
        }

        SyncLayoutFromView();
        var after = MindmapSnapshotHelper.Clone(_currentMindmap);
        _historyManager.Push(new MindmapStateOperation("Auto layout", before, after, RestoreMindmapStateAsync));
        foreach (var node in Nodes)
            await _mindmapService.UpdateNodeLayoutAsync(_currentMindmap.Id, node.Id, node.X, node.Y);
    }

    private Dictionary<string, List<NodeViewModel>> GetHierarchyChildren()
    {
        var nodeById = Nodes.ToDictionary(n => n.Id);
        var children = new Dictionary<string, List<NodeViewModel>>();
        foreach (var e in _currentMindmap!.Edges.Where(x => x.Kind == MindmapEdgeKind.Hierarchy))
        {
            if (!nodeById.TryGetValue(e.FromId, out var from) || !nodeById.TryGetValue(e.ToId, out var to)) continue;
            if (!children.ContainsKey(from.Id)) children[from.Id] = new List<NodeViewModel>();
            children[from.Id].Add(to);
        }
        return children;
    }

    private static double LayoutTreeVertical(NodeViewModel node, double x, double y, IReadOnlyDictionary<string, List<NodeViewModel>> children, HashSet<string> visited)
    {
        if (visited.Contains(node.Id)) return y;
        visited.Add(node.Id);
        node.X = x;
        node.Y = y;
        if (!children.TryGetValue(node.Id, out var childList) || childList.Count == 0)
            return y + LayoutTreeVerticalVSpacing;
        double childX = x + LayoutTreeVerticalHSpacing;
        double childY = y;
        double firstChildY = y;
        foreach (var child in childList)
            childY = LayoutTreeVertical(child, childX, childY, children, visited);
        double lastChildBottom = childY - LayoutTreeVerticalVSpacing;
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
            return x + LayoutTreeHorizontalHSpacing;
        double childY = y + LayoutTreeHorizontalVSpacing;
        double childX = x;
        double firstChildX = x;
        foreach (var child in childList)
            childX = LayoutTreeHorizontal(child, childX, childY, children, visited);
        double midX = (firstChildX + (childX - LayoutTreeHorizontalHSpacing)) / 2;
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
