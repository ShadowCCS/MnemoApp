using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.Services;

namespace Mnemo.UI.Modules.Notes.Views;

public partial class NotePdfExportOverlay : UserControl
{
    private Note _note = null!;
    private INotePdfExportService _pdfExport = null!;
    private ILocalizationService _loc = null!;
    private IOverlayService? _overlayService;
    private CancellationTokenSource? _previewCts;
    private DispatcherTimer? _debounce;
    private bool _chromeApplied;
    private EventHandler? _themeChangedHandler;

    public Action? CloseRequested { get; set; }

    public NotePdfExportOverlay()
    {
        InitializeComponent();
    }

    public void InitializeForNote(Note noteSnapshot)
    {
        if (noteSnapshot == null) throw new ArgumentNullException(nameof(noteSnapshot));
        _note = noteSnapshot;
        var app = Application.Current as App ?? throw new InvalidOperationException("Application not available.");
        var sp = app.Services ?? throw new InvalidOperationException("Application services not available.");
        _pdfExport = sp.GetRequiredService<INotePdfExportService>();
        _loc = sp.GetRequiredService<ILocalizationService>();
        _overlayService = sp.GetService<IOverlayService>();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_pdfExport == null) return;
        if (!_chromeApplied)
        {
            _chromeApplied = true;
            ApplyLocalizedChrome();
            PaperCombo.ItemsSource = new[] { _loc.T("PdfPaperA4", "Notes"), _loc.T("PdfPaperLetter", "Notes") };
            PaperCombo.SelectedIndex = 0;
            MarginCombo.ItemsSource = new[] { _loc.T("PdfMarginNormal", "Notes"), _loc.T("PdfMarginNarrow", "Notes") };
            MarginCombo.SelectedIndex = 0;
            PageNumberPositionCombo.ItemsSource = new[]
            {
                _loc.T("PdfPageNumberNone", "Notes"),
                _loc.T("PdfPageNumberLeft", "Notes"),
                _loc.T("PdfPageNumberCenter", "Notes"),
                _loc.T("PdfPageNumberRight", "Notes")
            };
            PageNumberPositionCombo.SelectedIndex = 2;
            PageNumberFormatCombo.ItemsSource = new[]
            {
                _loc.T("PdfPageNumberFormatCurrentTotal", "Notes"),
                _loc.T("PdfPageNumberFormatCurrent", "Notes")
            };
            PageNumberFormatCombo.SelectedIndex = 0;
            FontSizeCombo.ItemsSource = new[]
            {
                $"{_loc.T("PdfFontSmall", "Notes")} (10 pt)",
                $"{_loc.T("PdfFontMedium", "Notes")} (11 pt)",
                $"{_loc.T("PdfFontLarge", "Notes")} (12 pt)",
                $"{_loc.T("PdfFontExtraLarge", "Notes")} (14 pt)"
            };
            FontSizeCombo.SelectedIndex = 1;
            IncludeTitleCheck.IsCheckedChanged += (_, _) => SchedulePreviewRebuild();
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            _debounce.Tick += (_, _) =>
            {
                _debounce!.Stop();
                _ = RebuildPreviewAsync();
            };
        }

        if (Application.Current is { } app && _themeChangedHandler == null)
        {
            _themeChangedHandler = OnApplicationThemeChanged;
            app.ActualThemeVariantChanged += _themeChangedHandler;
        }

        _ = RebuildPreviewAsync();
    }

    private void OnApplicationThemeChanged(object? sender, EventArgs e) => SchedulePreviewRebuild();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Application.Current is { } app && _themeChangedHandler != null)
        {
            app.ActualThemeVariantChanged -= _themeChangedHandler;
            _themeChangedHandler = null;
        }

        _debounce?.Stop();
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        ClearPreviewImages(disposeBitmaps: true);
        base.OnDetachedFromVisualTree(e);
    }

    private void ApplyLocalizedChrome()
    {
        TitleText.Text = _loc.T("PdfExportTitle", "Notes");
        DescriptionText.Text = _loc.T("PdfExportDescription", "Notes");
        PaperLabel.Text = _loc.T("PdfPaperSize", "Notes");
        MarginLabel.Text = _loc.T("PdfMargins", "Notes");
        FontSizeLabel.Text = _loc.T("PdfBaseFontSize", "Notes");
        IncludeTitleCheck.Content = _loc.T("PdfIncludeNoteTitle", "Notes");
        PageNumberPositionLabel.Text = _loc.T("PdfPageNumberPosition", "Notes");
        PageNumberFormatLabel.Text = _loc.T("PdfPageNumberFormat", "Notes");
        ExportButton.Content = _loc.T("PdfExport", "Notes");
        PreviewCaption.Text = _loc.T("PdfPreviewCaption", "Notes");
        PreviewStatusText.Text = string.Empty;
    }

    private NotePdfExportOptions BuildOptions()
    {
        var paper = PaperCombo.SelectedIndex <= 0 ? NotePdfPaperKind.A4 : NotePdfPaperKind.Letter;
        var margin = MarginCombo.SelectedIndex <= 0 ? NotePdfMarginPreset.Normal : NotePdfMarginPreset.Narrow;
        var fontPt = FontSizeCombo.SelectedIndex switch
        {
            0 => 10f,
            2 => 12f,
            3 => 14f,
            _ => 11f
        };
        return new NotePdfExportOptions
        {
            Paper = paper,
            Margin = margin,
            IncludeNoteTitle = IncludeTitleCheck.IsChecked != false,
            BaseFontSizePt = fontPt,
            PageNumberAlignment = PageNumberPositionCombo.SelectedIndex switch
            {
                0 => NotePdfPageNumberAlignment.None,
                1 => NotePdfPageNumberAlignment.Left,
                3 => NotePdfPageNumberAlignment.Right,
                _ => NotePdfPageNumberAlignment.Center
            },
            PageNumberFormat = PageNumberFormatCombo.SelectedIndex == 1
                ? NotePdfPageNumberFormat.CurrentPage
                : NotePdfPageNumberFormat.CurrentAndTotalPages,
            PreviewRasterDpi = 120,
            BackgroundSwatchHexByName = PdfExportDawnSwatchResolver.GetBackgroundSwatchHexByName()
        };
    }

    private void OnSettingChanged(object? sender, SelectionChangedEventArgs e) => SchedulePreviewRebuild();

    private void SchedulePreviewRebuild()
    {
        if (_debounce == null) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task RebuildPreviewAsync()
    {
        if (_pdfExport == null) return;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;
        PreviewStatusText.Text = _loc.T("PdfPreviewLoading", "Notes");
        try
        {
            var options = BuildOptions();
            var pages = await _pdfExport.GeneratePreviewPngPagesAsync(_note, options, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
            var newSheets = pages.Select(CreatePreviewSheet).ToList();
            var oldSheets = PreviewHost.Children.ToList();
            PreviewHost.Children.Clear();
            foreach (var sheet in newSheets)
                PreviewHost.Children.Add(sheet);
            DisposePreviewControls(oldSheets);
            PreviewStatusText.Text = string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PreviewStatusText.Text = _loc.T("PdfPreviewError", "Notes") + ": " + ex.Message;
        }
    }

    private Border CreatePreviewSheet(byte[] png)
    {
        using var ms = new MemoryStream(png);
        var bmp = new Bitmap(ms);
        var img = new Image
        {
            Source = bmp,
            Stretch = Stretch.Uniform,
            MaxWidth = 600
        };
        var border = new Border
        {
            Child = img,
            Background = Brushes.White,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1)
        };

        if (Application.Current?.TryFindResource("BorderBrush", ActualThemeVariant, out var borderBrush) == true
            && borderBrush is IBrush bb)
            border.BorderBrush = bb;
        else if (Application.Current?.TryFindResource("BorderBrush", out var borderBrush2) == true
                 && borderBrush2 is IBrush bb2)
            border.BorderBrush = bb2;

        if (Application.Current?.TryFindResource("MindmapToolbarBoxShadow", ActualThemeVariant, out var shadowRes) == true
            && shadowRes is BoxShadows shadows)
            border.BoxShadow = shadows;
        else if (Application.Current?.TryFindResource("MindmapToolbarBoxShadow", out var shadow2) == true
                 && shadow2 is BoxShadows shadows2)
            border.BoxShadow = shadows2;

        return border;
    }

    private void ClearPreviewImages(bool disposeBitmaps)
    {
        if (!disposeBitmaps)
        {
            PreviewHost.Children.Clear();
            return;
        }
        foreach (var c in PreviewHost.Children.ToList())
            DisposePreviewControl(c);
        PreviewHost.Children.Clear();
    }

    private static void DisposePreviewControls(System.Collections.Generic.IEnumerable<Control> controls)
    {
        foreach (var control in controls)
            DisposePreviewControl(control);
    }

    private static void DisposePreviewControl(Control control)
    {
        if (control is Border b && b.Child is Image img && img.Source is Bitmap bmp)
            bmp.Dispose();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (_pdfExport == null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider == null) return;
        var title = string.IsNullOrWhiteSpace(_note.Title) ? "note" : _note.Title.Trim();
        foreach (var c in global::System.IO.Path.GetInvalidFileNameChars())
            title = title.Replace(c, '_');
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = _loc.T("PdfExportPickerTitle", "Notes"),
            SuggestedFileName = title + ".pdf",
            DefaultExtension = "pdf",
            FileTypeChoices =
            [
                new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }
            ]
        }).ConfigureAwait(true);
        if (file == null) return;
        try
        {
            var bytes = await _pdfExport.GeneratePdfAsync(_note, BuildOptions()).ConfigureAwait(true);
            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            await stream.WriteAsync(bytes).ConfigureAwait(true);
            if (_overlayService != null)
                await _overlayService.CreateDialogAsync(
                    _loc.T("PdfExportCompleteTitle", "Notes"),
                    _loc.T("PdfExportCompleteMessage", "Notes")).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (_overlayService != null)
                await _overlayService.CreateDialogAsync(
                    _loc.T("PdfExportFailedTitle", "Notes"),
                    ex.Message).ConfigureAwait(true);
        }
    }
}
