using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.TwoColumn;

public partial class TwoColumnBlockComponent : BlockComponentBase
{
    private bool _splitDragging;
    private BlockViewModel? _subscribedVm;

    public TwoColumnBlockComponent()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyRatioFromViewModel();
        DataContextChanged += OnDataContextChanged;
        if (SplitterBorder != null)
        {
            SplitterBorder.PointerEntered += (_, _) =>
            {
                _splitterPointerOver = true;
                RefreshSplitterGripOpacity();
            };
            SplitterBorder.PointerExited += (_, _) =>
            {
                _splitterPointerOver = false;
                RefreshSplitterGripOpacity();
            };
        }
    }

    private bool _splitterPointerOver;

    private void RefreshSplitterGripOpacity()
    {
        if (SplitterGrip == null) return;
        SplitterGrip.Opacity = _splitDragging || _splitterPointerOver ? 0.42 : 0;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
        _subscribedVm = DataContext as BlockViewModel;
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged += OnVmPropertyChanged;
        ApplyRatioFromViewModel();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BlockViewModel.ColumnSplitRatio) or nameof(BlockViewModel.Meta))
            ApplyRatioFromViewModel();
    }

    public override Control? GetInputControl() => null;

    private void ApplyRatioFromViewModel()
    {
        if (DataContext is not BlockViewModel vm || RootGrid == null) return;
        var r = vm.ColumnSplitRatio;
        RootGrid.ColumnDefinitions[0].Width = new GridLength(r, GridUnitType.Star);
        RootGrid.ColumnDefinitions[2].Width = new GridLength(1 - r, GridUnitType.Star);
    }

    private void Splitter_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not BlockViewModel vm || RootGrid == null) return;
        _splitDragging = true;
        RefreshSplitterGripOpacity();
        e.Pointer.Capture(SplitterBorder);
        var editor = this.GetVisualAncestors().OfType<BlockEditor>().FirstOrDefault();
        editor?.BeginColumnSplitResize();
    }

    private void Splitter_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_splitDragging || DataContext is not BlockViewModel vm || RootGrid == null) return;
        var w = RootGrid.Bounds.Width;
        if (w <= 1) return;
        var x = e.GetPosition(RootGrid).X;
        vm.ColumnSplitRatio = x / w;
        ApplyRatioFromViewModel();
    }

    private void Splitter_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_splitDragging) return;
        _splitDragging = false;
        e.Pointer.Capture(null);
        RefreshSplitterGripOpacity();
        var editor = this.GetVisualAncestors().OfType<BlockEditor>().FirstOrDefault();
        editor?.CommitColumnSplitResize();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
            _subscribedVm = null;
        }
        base.OnDetachedFromVisualTree(e);
    }
}
