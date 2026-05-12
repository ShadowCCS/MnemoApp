using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Services;
using Mnemo.Core.Sketch;
using Mnemo.UI.Components.Overlays;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Sketch;

public partial class SketchBlockComponent : BlockComponentBase
{
    private readonly SketchCompiler _compiler = new();
    private string _lastGoodSvg = string.Empty;
    private BlockViewModel? _subscribedViewModel;
    private IOverlayService? _overlayService;
    private string? _overlayId;

    public SketchBlockComponent()
    {
        _overlayService = (Application.Current as App)?.Services?.GetService<IOverlayService>();
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => RefreshPreview();
    }

    public override Control? GetInputControl() => null;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel != null)
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _subscribedViewModel = DataContext as BlockViewModel;
        if (_subscribedViewModel != null)
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;

        RefreshPreview();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_subscribedViewModel != null)
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = null;
        DataContextChanged -= OnDataContextChanged;
        CloseOverlay();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlockViewModel.Content))
            RefreshPreview();
    }

    private void RefreshPreview()
    {
        var source = (DataContext as BlockViewModel)?.Content ?? string.Empty;
        var result = _compiler.CompileToSvg(source);
        var errors = result.Diagnostics
            .Where(d => d.Severity == SketchDiagnosticSeverity.Error)
            .ToArray();

        if (errors.Length == 0)
        {
            _lastGoodSvg = result.Svg;
            Preview.Svg = result.Svg;
            DiagnosticsText.IsVisible = false;
            DiagnosticsText.Text = string.Empty;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lastGoodSvg))
            Preview.Svg = _lastGoodSvg;
        DiagnosticsText.Text = string.Join("\n", errors.Select(d => d.Message));
        DiagnosticsText.IsVisible = true;
    }

    private void RootBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        OpenEditorOverlay();
        e.Handled = true;
    }

    private void OpenEditorOverlay()
    {
        if (_subscribedViewModel == null)
            return;

        _overlayService ??= (Application.Current as App)?.Services?.GetService<IOverlayService>();
        if (_overlayService == null)
            return;

        CloseOverlay();

        var overlay = new SketchEditorOverlay
        {
            Source = _subscribedViewModel.Content ?? string.Empty
        };
        overlay.SaveRequested += SaveOverlaySource;
        overlay.CancelRequested += CloseOverlay;

        _overlayId = _overlayService.CreateOverlay(
            overlay,
            new OverlayOptions
            {
                ShowBackdrop = true,
                CloseOnOutsideClick = false,
                CloseOnEscape = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            },
            "SketchEditor");
    }

    private void SaveOverlaySource(string source)
    {
        if (_subscribedViewModel == null)
            return;

        _subscribedViewModel.NotifyStructuralChangeStarting();
        _subscribedViewModel.Content = source;
        _subscribedViewModel.NotifyStructuralChangeCompleted("Update Sketch diagram");
        RefreshPreview();
        CloseOverlay();
    }

    private void CloseOverlay()
    {
        if (string.IsNullOrEmpty(_overlayId) || _overlayService == null)
            return;

        _overlayService.CloseOverlay(_overlayId);
        _overlayId = null;
    }
}
