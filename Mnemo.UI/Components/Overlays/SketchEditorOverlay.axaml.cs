using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Mnemo.Core.Sketch;
using Mnemo.UI.Services;

namespace Mnemo.UI.Components.Overlays;

public partial class SketchEditorOverlay : UserControl
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 6;
    private const double ZoomStep = 1.2;
    // Shell uses explicit W/H from host bounds; Max* alone does not expand (Border sizes to children).
    private const double ShellViewportWidthScale = 0.88;
    private const double ShellViewportHeightScale = 0.84;
    private const double ShellMaxWidthAbs = 2560;
    private const double ShellMaxHeightAbs = 1440;
    private const double ShellRootMarginTotal = 16;

    private readonly SketchCompiler _compiler = new();
    private bool _isPanning;
    private Point _lastPanPoint;
    private Size _lastShellCap;

    public SketchEditorOverlay()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => UpdateShellViewportCap();
    }

    public event Action<string>? SaveRequested;

    public event Action? CancelRequested;

    public string Source
    {
        get => SourceEditor.Text ?? string.Empty;
        set
        {
            SourceEditor.Text = value ?? string.Empty;
            RefreshPreview();
        }
    }

    private TextBox SourceEditor =>
        this.FindControl<TextBox>("SourceTextBox")
        ?? throw new InvalidOperationException("Sketch editor source TextBox was not loaded.");

    private Mnemo.UI.Controls.SketchSvgView PreviewView =>
        this.FindControl<Mnemo.UI.Controls.SketchSvgView>("Preview")
        ?? throw new InvalidOperationException("Sketch editor preview was not loaded.");

    private TextBlock DiagnosticsBlock =>
        this.FindControl<TextBlock>("DiagnosticsText")
        ?? throw new InvalidOperationException("Sketch editor diagnostics text was not loaded.");

    private TextBlock SourceHighlight =>
        this.FindControl<TextBlock>("SourceHighlightBlock")
        ?? throw new InvalidOperationException("Sketch editor source highlight block was not loaded.");

    private Mnemo.UI.Controls.TextSelectionBackgroundLayer SourceSelectionBackgroundLayer =>
        this.FindControl<Mnemo.UI.Controls.TextSelectionBackgroundLayer>("SourceSelectionBackground")
        ?? throw new InvalidOperationException("Sketch editor selection background was not loaded.");

    private Button SaveDiagramButton =>
        this.FindControl<Button>("SaveButton")
        ?? throw new InvalidOperationException("Sketch editor save button was not loaded.");

    private Border PreviewSurfaceControl =>
        this.FindControl<Border>("PreviewSurface")
        ?? throw new InvalidOperationException("Sketch editor preview surface was not loaded.");

    private TextBlock ZoomTextBlock =>
        this.FindControl<TextBlock>("ZoomText")
        ?? throw new InvalidOperationException("Sketch editor zoom text was not loaded.");

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        SourceEditor.KeyDown += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(SyncSourceSelectionBackground, Avalonia.Threading.DispatcherPriority.Input);
        SourceEditor.KeyUp += (_, _) => SyncSourceSelectionBackground();
        SourceEditor.PointerPressed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(SyncSourceSelectionBackground, Avalonia.Threading.DispatcherPriority.Input);
        SourceEditor.PointerMoved += (_, _) => SyncSourceSelectionBackground();
        SourceEditor.PointerReleased += (_, _) => SyncSourceSelectionBackground();
        SourceEditor.GotFocus += (_, _) => SyncSourceSelectionBackground();
    }

    private void SourceTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshPreview();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateShellViewportCap();
        ResetPreviewView();
    }

    private void UpdateShellViewportCap()
    {
        var shell = this.FindControl<Border>("ShellCard");
        var top = TopLevel.GetTopLevel(this);
        if (shell == null || top == null)
            return;

        var client = top.ClientSize;
        var availW = Math.Max(Bounds.Width - ShellRootMarginTotal, 80);
        var availH = Math.Max(Bounds.Height - ShellRootMarginTotal, 80);
        if (Bounds.Width < 80 || Bounds.Height < 80)
        {
            availW = Math.Max(client.Width - ShellRootMarginTotal, 80);
            availH = Math.Max(client.Height - ShellRootMarginTotal, 80);
        }

        var next = new Size(
            Math.Clamp(availW * ShellViewportWidthScale, 280, ShellMaxWidthAbs),
            Math.Clamp(availH * ShellViewportHeightScale, 200, ShellMaxHeightAbs));

        if (Math.Abs(next.Width - _lastShellCap.Width) < 0.5 && Math.Abs(next.Height - _lastShellCap.Height) < 0.5)
            return;

        _lastShellCap = next;
        shell.MaxWidth = next.Width;
        shell.MaxHeight = next.Height;
        shell.Width = next.Width;
        shell.Height = next.Height;
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        SaveRequested?.Invoke(Source);
        e.Handled = true;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
        e.Handled = true;
    }

    private void RefreshPreview()
    {
        var source = Source;
        SyncSourceSelectionBackground();
        RefreshSourceHighlight(source);
        var result = _compiler.CompileToSvg(source);
        PreviewView.Svg = result.Svg;

        var errors = result.Diagnostics
            .Where(d => d.Severity == SketchDiagnosticSeverity.Error)
            .Select(d => d.Message)
            .ToArray();

        DiagnosticsBlock.Text = string.Join("\n", errors);
        DiagnosticsBlock.IsVisible = errors.Length > 0;
        SaveDiagramButton.IsEnabled = errors.Length == 0;
        UpdateZoomText();
    }

    private void RefreshSourceHighlight(string source)
    {
        SourceHighlight.Inlines?.Clear();
        var def = ThemeBrush("SyntaxCodeDefaultBrush");
        SketchSyntaxHighlighter.ApplyToTextBlock(SourceHighlight, source, def);
    }

    private void SyncSourceSelectionBackground()
    {
        var editor = SourceEditor;
        SourceSelectionBackgroundLayer.Text = editor.Text ?? string.Empty;
        SourceSelectionBackgroundLayer.SelectionStart = editor.SelectionStart;
        SourceSelectionBackgroundLayer.SelectionEnd = editor.SelectionEnd;
    }

    private static IBrush ThemeBrush(string key)
    {
        var app = Application.Current;
        return app != null && app.TryGetResource(key, app.ActualThemeVariant, out var resource) && resource is IBrush brush
            ? brush
            : Brushes.Gainsboro;
    }

    private void PreviewSurface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PreviewSurfaceControl).Properties.IsLeftButtonPressed)
            return;

        _isPanning = true;
        _lastPanPoint = e.GetPosition(PreviewSurfaceControl);
        e.Pointer.Capture(PreviewSurfaceControl);
        e.Handled = true;
    }

    private void PreviewSurface_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning || !ReferenceEquals(e.Pointer.Captured, PreviewSurfaceControl))
            return;

        var point = e.GetPosition(PreviewSurfaceControl);
        var delta = point - _lastPanPoint;
        PreviewView.PanX += delta.X;
        PreviewView.PanY += delta.Y;
        _lastPanPoint = point;
        e.Handled = true;
    }

    private void PreviewSurface_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndPan(e.Pointer);
        e.Handled = true;
    }

    private void PreviewSurface_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isPanning = false;
    }

    private void PreviewSurface_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var factor = e.Delta.Y > 0 ? ZoomStep : 1 / ZoomStep;
        ApplyZoom(PreviewView.Zoom * factor, e.GetPosition(PreviewView));
        e.Handled = true;
    }

    private void ZoomInButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyZoom(PreviewView.Zoom * ZoomStep, CenterOfPreview());
        e.Handled = true;
    }

    private void ZoomOutButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyZoom(PreviewView.Zoom / ZoomStep, CenterOfPreview());
        e.Handled = true;
    }

    private void ResetViewButton_Click(object? sender, RoutedEventArgs e)
    {
        ResetPreviewView();
        e.Handled = true;
    }

    private void ApplyZoom(double requestedZoom, Point anchor)
    {
        var nextZoom = Math.Clamp(requestedZoom, MinZoom, MaxZoom);
        PreviewView.ZoomAround(nextZoom, anchor);
        UpdateZoomText();
    }

    private void ResetPreviewView()
    {
        PreviewView.Zoom = 1;
        PreviewView.PanX = 0;
        PreviewView.PanY = 0;
        UpdateZoomText();
    }

    private void UpdateZoomText()
    {
        ZoomTextBlock.Text = $"{PreviewView.Zoom * 100:0}%";
    }

    private Point CenterOfPreview() => new(PreviewView.Bounds.Width / 2, PreviewView.Bounds.Height / 2);

    private void EndPan(IPointer pointer)
    {
        _isPanning = false;
        if (ReferenceEquals(pointer.Captured, PreviewSurfaceControl))
            pointer.Capture(null);
    }
}
