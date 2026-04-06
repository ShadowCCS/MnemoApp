using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using Mnemo.Core.Formatting;
using Mnemo.Core.Services;
using System;

namespace Mnemo.UI.Components.BlockEditor.FormattingToolbar;

public partial class InlineFormattingToolbar : UserControl
{
    private Ellipse? _colorSwatch;
    private string? _colorOverlayId;
    private IOverlayService? _overlayService;
    private ColorSwatchPopup? _currentColorPopup;
    private DateTime _lastInteractionUtc = DateTime.MinValue;

    /// <summary>Raised when a formatting action (Bold, Italic, etc.) is requested.</summary>
    public event Action<InlineFormatKind>? FormatRequested;

    /// <summary>Raised when a background color is selected from the color dropdown.</summary>
    public event Action<string>? BackgroundColorRequested;

    private const string ActiveClass = "FormattingToolbarIconActive";

    /// <summary>Headings use forced bold; the Bold control is non-interactive while this is false.</summary>
    public void SetBoldButtonEnabled(bool enabled)
    {
        if (this.FindControl<Button>("BoldButton") is { } btn)
            btn.IsEnabled = enabled;
    }

    /// <summary>Updates toggle state of format buttons from the current selection.</summary>
    public void UpdateFormatState(bool bold, bool italic, bool underline, bool strikethrough, bool highlight, string? backgroundColor, bool hasLink)
    {
        SetButtonActive("BoldButton", bold);
        SetButtonActive("ItalicButton", italic);
        SetButtonActive("UnderlineButton", underline);
        SetButtonActive("UnlinkButton", hasLink);
        SetButtonActive("StrikethroughButton", strikethrough);
        SetButtonActive("HighlightButton", highlight);
        if (this.FindControl<Ellipse>("ColorSwatch") is { } swatch && backgroundColor != null)
        {
            Color newColor = Colors.Transparent;
            if (Color.TryParse(backgroundColor, out var c))
            {
                newColor = c;
            }
            else if (backgroundColor.StartsWith("swatch", StringComparison.OrdinalIgnoreCase) && Application.Current != null)
            {
                var key = "ColorSwatch" + backgroundColor.Substring(6);
                if (Application.Current.TryFindResource(key, out var res) && res is Color rc)
                    newColor = rc;
            }

            if (newColor != Colors.Transparent && !(swatch.Fill is SolidColorBrush solidBrush && solidBrush.Color == newColor))
            {
                swatch.Fill = new SolidColorBrush(newColor);
            }
        }
    }

    private void SetButtonActive(string name, bool active)
    {
        if (this.FindControl<Button>(name) is not { } btn) return;
        var hasClass = btn.Classes.Contains(ActiveClass);
        if (active && !hasClass)
            btn.Classes.Add(ActiveClass);
        else if (!active && hasClass)
            btn.Classes.Remove(ActiveClass);
    }

    public bool IsInteractingWithToolbar
    {
        get
        {
            var recentInteraction = (DateTime.UtcNow - _lastInteractionUtc).TotalMilliseconds < 400;
            return recentInteraction || IsPointerOver || (_currentColorPopup?.IsPointerOver ?? false);
        }
    }

    public bool IsEventFromToolbarOverlay(object? source)
    {
        if (source is not Visual sourceVisual)
            return false;

        if (IsDescendantOf(sourceVisual, this))
            return true;

        if (_currentColorPopup != null && IsDescendantOf(sourceVisual, _currentColorPopup))
            return true;

        return false;
    }

    public InlineFormattingToolbar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        AddHandler(InputElement.PointerPressedEvent, OnToolbarPointerPressed, RoutingStrategies.Tunnel);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _colorSwatch = this.FindControl<Ellipse>("ColorSwatch");
        _overlayService = (Application.Current as App)?.Services?.GetService(typeof(IOverlayService)) as IOverlayService;

        var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        string T(string key, string ns = "NotesEditor") => loc?.T(key, ns) ?? key;

        var assistLabel = this.FindControl<TextBlock>("AssistLabel");
        if (assistLabel != null) assistLabel.Text = T("Assist");

        var colorLabel = this.FindControl<TextBlock>("ColorLabel");
        if (colorLabel != null) colorLabel.Text = T("Color");

        if (this.FindControl<Button>("UnlinkButton") is { } linkBtn)
            ToolTip.SetTip(linkBtn, T("LinkTooltip"));

        Loaded -= OnLoaded;
    }

    private void OnAssistClick(object? sender, RoutedEventArgs e) { }

    private void OnColorClick(object? sender, RoutedEventArgs e)
    {
        _lastInteractionUtc = DateTime.UtcNow;
        if (_overlayService == null) return;

        CloseColorPopup();

        var popup = new ColorSwatchPopup();
        popup.ColorSelected += OnSwatchColorSelected;
        _currentColorPopup = popup;

        var colorButton = this.FindControl<Button>("ColorButton");
        var options = new OverlayOptions
        {
            ShowBackdrop = false,
            CloseOnOutsideClick = true,
            AnchorControl = colorButton ?? (object)this,
            AnchorPosition = AnchorPosition.BottomLeft,
            AnchorOffset = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };

        _colorOverlayId = _overlayService.CreateOverlay(popup, options, "ColorSwatchPopup");
    }

    private void OnSwatchColorSelected(string colorOrSwatch)
    {
        _lastInteractionUtc = DateTime.UtcNow;
        CloseColorPopup();
        if (_colorSwatch != null)
        {
            Color newColor = Colors.Transparent;
            if (Color.TryParse(colorOrSwatch, out var c))
            {
                newColor = c;
            }
            else if (colorOrSwatch.StartsWith("swatch", StringComparison.OrdinalIgnoreCase) && Application.Current != null)
            {
                var key = "ColorSwatch" + colorOrSwatch.Substring(6);
                if (Application.Current.TryFindResource(key, out var res) && res is Color rc)
                    newColor = rc;
            }

            if (newColor != Colors.Transparent)
            {
                _colorSwatch.Fill = new SolidColorBrush(newColor);
            }
        }
        BackgroundColorRequested?.Invoke(colorOrSwatch);
    }

    private void CloseColorPopup()
    {
        if (!string.IsNullOrEmpty(_colorOverlayId) && _overlayService != null)
        {
            _overlayService.CloseOverlay(_colorOverlayId);
            _colorOverlayId = null;
        }

        _currentColorPopup = null;
    }

    private void OnBoldClick(object? sender, RoutedEventArgs e) => FormatRequested?.Invoke(InlineFormatKind.Bold);
    private void OnItalicClick(object? sender, RoutedEventArgs e) => FormatRequested?.Invoke(InlineFormatKind.Italic);
    private void OnUnderlineClick(object? sender, RoutedEventArgs e) => FormatRequested?.Invoke(InlineFormatKind.Underline);
    private void OnUnlinkClick(object? sender, RoutedEventArgs e) => FormatRequested?.Invoke(InlineFormatKind.Link);
    private void OnStrikethroughClick(object? sender, RoutedEventArgs e) => FormatRequested?.Invoke(InlineFormatKind.Strikethrough);
    private void OnHighlightClick(object? sender, RoutedEventArgs e) => FormatRequested?.Invoke(InlineFormatKind.Highlight);
    private void OnSubscriptClick(object? sender, RoutedEventArgs e) { }
    private void OnSuperscriptClick(object? sender, RoutedEventArgs e) { }

    private void OnToolbarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _lastInteractionUtc = DateTime.UtcNow;
    }

    private static bool IsDescendantOf(Visual source, Visual ancestor)
    {
        Visual? current = source;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
            current = current.GetVisualParent();
        }
        return false;
    }
}
