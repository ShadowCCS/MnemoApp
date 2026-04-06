using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Mnemo.UI.Components.BlockEditor;

public partial class SplitBlockRowView : UserControl
{
    private bool _splitDragging;
    private TwoColumnBlockViewModel? _subscribedTwoColumn;

    public SplitBlockRowView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyRatioFromTwoColumn();
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
        if (_subscribedTwoColumn != null)
            _subscribedTwoColumn.PropertyChanged -= OnTwoColumnPropertyChanged;
        _subscribedTwoColumn = (DataContext as SplitBlockRowViewModel)?.TwoColumn;
        if (_subscribedTwoColumn != null)
            _subscribedTwoColumn.PropertyChanged += OnTwoColumnPropertyChanged;
        ApplyRatioFromTwoColumn();
    }

    private void OnTwoColumnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BlockViewModel.ColumnSplitRatio) or nameof(BlockViewModel.Meta))
            ApplyRatioFromTwoColumn();
    }

    private void ApplyRatioFromTwoColumn()
    {
        if (DataContext is not SplitBlockRowViewModel row || RootGrid == null) return;
        var r = row.TwoColumn.ColumnSplitRatio;
        RootGrid.ColumnDefinitions[0].Width = new GridLength(r, GridUnitType.Star);
        RootGrid.ColumnDefinitions[2].Width = new GridLength(1 - r, GridUnitType.Star);
    }

    private void Splitter_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not SplitBlockRowViewModel || RootGrid == null) return;
        _splitDragging = true;
        RefreshSplitterGripOpacity();
        e.Pointer.Capture(SplitterBorder);
        this.GetVisualAncestors().OfType<BlockEditor>().FirstOrDefault()?.BeginColumnSplitResize();
    }

    private void Splitter_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_splitDragging || DataContext is not SplitBlockRowViewModel row || RootGrid == null) return;
        var w = RootGrid.Bounds.Width;
        if (w <= 1) return;
        row.TwoColumn.ColumnSplitRatio = e.GetPosition(RootGrid).X / w;
        ApplyRatioFromTwoColumn();
    }

    private void Splitter_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_splitDragging) return;
        _splitDragging = false;
        e.Pointer.Capture(null);
        RefreshSplitterGripOpacity();
        this.GetVisualAncestors().OfType<BlockEditor>().FirstOrDefault()?.CommitColumnSplitResize();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_subscribedTwoColumn != null)
        {
            _subscribedTwoColumn.PropertyChanged -= OnTwoColumnPropertyChanged;
            _subscribedTwoColumn = null;
        }
        base.OnDetachedFromVisualTree(e);
    }
}
