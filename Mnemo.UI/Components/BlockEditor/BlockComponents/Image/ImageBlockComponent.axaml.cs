using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI;
using Mnemo.UI.Components.BlockEditor;
using Mnemo.UI.Services;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Image;

public partial class ImageBlockComponent : BlockComponentBase
{
    private readonly IImageAssetService? _imageAssetService;
    private readonly ILocalizationService? _loc;

    private Bitmap? _currentBitmap;
    private bool _isResizing;
    private double _resizeDragStartX;
    private double _resizeDragStartWidth;

    /// <summary>
    /// BlockContainer padding (16) + add column (30) + drag column (30). Content column = list row width minus this.
    /// See EditableBlock.axaml.
    /// </summary>
    private const double BlockItemContentChromeInset = 76;

    /// <summary>Hard cap so huge monitors do not allow absurd image widths.</summary>
    private const double MaxImageWidthCap = 1600;

    /// <summary>Hit target and selection padding around the image when loaded (Host is a Border).</summary>
    private const double LoadedImageHitPadding = 6;

    private string _captionWatermarkText = string.Empty;

    public ImageBlockComponent()
    {
        var services = (Application.Current as App)?.Services;
        _imageAssetService = services?.GetService(typeof(IImageAssetService)) as IImageAssetService;
        _loc = services?.GetService(typeof(ILocalizationService)) as ILocalizationService;

        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnHoverHostLayoutUpdated(object? sender, EventArgs e)
    {
        ClampImageWidthToViewport();
        UpdateCaptionHostWidth();
    }

    private string T(string key) => _loc?.T(key, "NotesEditor") ?? key;

    public override Control? GetInputControl() => null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (HoverHost != null)
            HoverHost.LayoutUpdated += OnHoverHostLayoutUpdated;
        if (CaptionBox != null)
        {
            CaptionBox.GotFocus += CaptionBox_GotFocus;
            CaptionBox.LostFocus += CaptionBox_LostFocus;
        }
        SetLocalizationStrings();
        SyncFromViewModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (HoverHost != null)
            HoverHost.LayoutUpdated -= OnHoverHostLayoutUpdated;
        if (CaptionBox != null)
        {
            CaptionBox.GotFocus -= CaptionBox_GotFocus;
            CaptionBox.LostFocus -= CaptionBox_LostFocus;
        }
        EndResizeSession();
        base.OnDetachedFromVisualTree(e);
        DisposeBitmap();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        SetLocalizationStrings();
        SyncFromViewModel();
    }

    private void SetLocalizationStrings()
    {
        _captionWatermarkText = T("ImageCaptionPlaceholder");
        if (PlaceholderLabel != null)
            PlaceholderLabel.Text = T("ImagePlaceholder");
        RefreshCaptionWatermark();

        void SetPh(MenuItem? m, string key) { if (m != null) m.Header = T(key); }
        SetPh(FlyoutReplaceItem, "ImageFlyoutReplace");
        SetPh(FlyoutCopyItem, "ImageFlyoutCopyImage");
        SetPh(FlyoutDuplicateItem, "ImageFlyoutDuplicate");
        SetPh(FlyoutDeleteItem, "ImageFlyoutDelete");
        SetPh(PhFlyoutReplaceItem, "ImageFlyoutReplace");
        SetPh(PhFlyoutCopyItem, "ImageFlyoutCopyImage");
        SetPh(PhFlyoutDuplicateItem, "ImageFlyoutDuplicate");
        SetPh(PhFlyoutDeleteItem, "ImageFlyoutDelete");

        void SetAlignTip(Button? b, string key)
        {
            if (b != null)
                ToolTip.SetTip(b, T(key));
        }

        SetAlignTip(PhAlignFlyoutLeftBtn, "ImageAlignLeftTooltip");
        SetAlignTip(PhAlignFlyoutCenterBtn, "ImageAlignCenterTooltip");
        SetAlignTip(PhAlignFlyoutRightBtn, "ImageAlignRightTooltip");
        SetAlignTip(LdAlignFlyoutLeftBtn, "ImageAlignLeftTooltip");
        SetAlignTip(LdAlignFlyoutCenterBtn, "ImageAlignCenterTooltip");
        SetAlignTip(LdAlignFlyoutRightBtn, "ImageAlignRightTooltip");
    }

    private void RefreshCaptionWatermark()
    {
        if (CaptionBox == null) return;
        var over = HoverHost?.IsPointerOver == true;
        CaptionBox.Watermark = over && string.IsNullOrEmpty(CaptionBox.Text)
            ? _captionWatermarkText
            : null;
    }

    private void SyncFromViewModel()
    {
        var vm = ViewModel;
        if (vm == null) return;

        var imagePath = GetMetaString(vm, "imagePath");
        var imageAlt = GetMetaString(vm, "imageAlt");
        var imageWidth = GetMetaDouble(vm, "imageWidth");

        UpdateCaptionBox(imageAlt);
        ApplyImageWidth(imageWidth);
        ClampImageWidthToViewport();

        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            ShowPlaceholder();
        }
        else
        {
            LoadBitmap(imagePath);
        }

        UpdateAlignButtonIcons();
    }

    private void LoadBitmap(string path)
    {
        DisposeBitmap();
        try
        {
            _currentBitmap = new Bitmap(path);
            DisplayImage.Source = _currentBitmap;
            ShowImageArea();
        }
        catch
        {
            ShowPlaceholder();
        }
    }

    private void DisposeBitmap()
    {
        if (_currentBitmap != null)
        {
            DisplayImage.Source = null;
            _currentBitmap.Dispose();
            _currentBitmap = null;
        }
    }

    private void ShowPlaceholder()
    {
        PlaceholderBorder.IsVisible = true;
        LoadedImageRow.IsVisible = false;
        LoadedToolbar.IsVisible = false;
        PlaceholderToolbar.IsVisible = false;
        ApplyHorizontalLayoutForContentState();
        UpdateCaptionHostWidth();
    }

    private void ShowImageArea()
    {
        PlaceholderBorder.IsVisible = false;
        LoadedImageRow.IsVisible = true;
        PlaceholderToolbar.IsVisible = false;
        ApplyHorizontalLayoutForContentState();
        UpdateCaptionHostWidth();
    }

    private void UpdateCaptionHostWidth()
    {
        if (CaptionHost == null) return;
        if (LoadedImageRow.IsVisible && DisplayImage != null && DisplayImage.Bounds.Width > 0)
        {
            CaptionHost.Width = DisplayImage.Bounds.Width;
            CaptionHost.HorizontalAlignment = HorizontalAlignment.Left; // caption hugs image
        }
        else
        {
            CaptionHost.Width = double.NaN;
            CaptionHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private void ApplyHorizontalLayoutForContentState()
    {
        // Alignment is handled by the parent EditableBlock (this.HorizontalAlignment).
        // Inner content stays shrink-wrapped so selection hugs the image.
        var isLoadedImage = LoadedImageRow.IsVisible;

        if (isLoadedImage)
        {
            HorizontalAlignment = HorizontalAlignment.Left; // shrink-wrap; parent EditableBlock aligns
            if (HoverHost != null)
            {
                HoverHost.HorizontalAlignment = HorizontalAlignment.Left;
                HoverHost.Padding = new Thickness(LoadedImageHitPadding);
            }
            if (RootGrid != null)
                RootGrid.HorizontalAlignment = HorizontalAlignment.Left;
            if (ImageContentRow != null)
                ImageContentRow.HorizontalAlignment = HorizontalAlignment.Left;
            if (LoadedImageRow != null)
                LoadedImageRow.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            if (HoverHost != null)
            {
                HoverHost.HorizontalAlignment = HorizontalAlignment.Stretch;
                HoverHost.Padding = default;
            }
            if (RootGrid != null)
                RootGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (ImageContentRow != null)
                ImageContentRow.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    // ── Hover host (menu visibility + keyboard focus) ─────────────────────────

    private void HoverHost_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (LoadedImageRow.IsVisible)
        {
            LoadedToolbar.IsVisible = true;
            LoadedToolbar.Opacity = 1;
        }
        if (PlaceholderBorder.IsVisible)
        {
            PlaceholderToolbar.IsVisible = true;
            PlaceholderToolbar.Opacity = 1;
        }
        RefreshCaptionWatermark();
    }

    private void HoverHost_PointerExited(object? sender, PointerEventArgs e)
    {
        if (FlyoutButton.Flyout?.IsOpen == true || FlyoutButtonPlaceholder.Flyout?.IsOpen == true)
            return;
        if (IsAnyAlignFlyoutOpen())
            return;

        LoadedToolbar.Opacity = 0;
        LoadedToolbar.IsVisible = false;
        PlaceholderToolbar.Opacity = 0;
        PlaceholderToolbar.IsVisible = false;
        RefreshCaptionWatermark();
    }

    private bool IsAnyAlignFlyoutOpen() =>
        AlignMenuButton.Flyout is { IsOpen: true } || AlignMenuButtonPlaceholder.Flyout is { IsOpen: true };

    private void HideAlignFlyouts()
    {
        if (AlignMenuButton.Flyout is FlyoutBase f1 && f1.IsOpen)
            f1.Hide();
        if (AlignMenuButtonPlaceholder.Flyout is FlyoutBase f2 && f2.IsOpen)
            f2.Hide();
    }

    private static string NormalizeImageAlign(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "center" => "center",
            "right" => "right",
            _ => "left",
        };

    private string GetImageAlignFromMeta()
    {
        var vm = ViewModel;
        if (vm == null) return "left";
        return NormalizeImageAlign(GetMetaString(vm, "imageAlign"));
    }

    private void UpdateAlignButtonIcons()
    {
        var align = GetImageAlignFromMeta();
        var path = align switch
        {
            "center" => "avares://Mnemo.UI/Icons/Editor/align-center.svg",
            "right" => "avares://Mnemo.UI/Icons/Editor/align-right.svg",
            _ => "avares://Mnemo.UI/Icons/Editor/align-left.svg",
        };
        if (AlignButtonIcon != null)
            AlignButtonIcon.SvgPath = path;
        if (AlignButtonIconPlaceholder != null)
            AlignButtonIconPlaceholder.SvgPath = path;
    }

    private void SetImageAlign(string value)
    {
        var vm = ViewModel;
        if (vm == null) return;
        var normalized = NormalizeImageAlign(value);
        vm.Meta["imageAlign"] = normalized;
        vm.NotifyContentChanged();
        UpdateAlignButtonIcons();
        // EditableBlock listens to Meta changes and updates its HorizontalAlignment
    }

    private void AlignPickLeft_Click(object? sender, RoutedEventArgs e)
    {
        SetImageAlign("left");
        HideAlignFlyouts();
    }

    private void AlignPickCenter_Click(object? sender, RoutedEventArgs e)
    {
        SetImageAlign("center");
        HideAlignFlyouts();
    }

    private void AlignPickRight_Click(object? sender, RoutedEventArgs e)
    {
        SetImageAlign("right");
        HideAlignFlyouts();
    }

    /// <summary>
    /// Keyboard focus for Delete/Back — only from image chrome, not from <see cref="HoverHost"/>
    /// (ancestor handlers run in the same route as toolbar <see cref="Button"/>s and break Flyout/MenuFlyout).
    /// </summary>
    private void ImageChrome_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        HoverHost?.Focus();
    }

    private void HoverHost_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0)
            return;

        if (e.Key == Key.Delete)
        {
            ViewModel?.NotifyStructuralChangeStarting();
            ViewModel?.RequestDelete();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            ViewModel?.NotifyStructuralChangeStarting();
            ViewModel?.RequestDeleteAndFocusAbove();
            e.Handled = true;
        }
    }

    // ── Import ────────────────────────────────────────────────────────────────

    private async void Placeholder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (IsVisualDescendantOf(e.Source as Visual, PlaceholderToolbar))
            return;
        e.Handled = true;
        await ImportImageAsync();
    }

    private static bool IsVisualDescendantOf(Visual? node, Visual? ancestor)
    {
        if (node == null || ancestor == null) return false;
        for (Visual? v = node; v != null; v = v.GetVisualParent())
        {
            if (ReferenceEquals(v, ancestor))
                return true;
        }

        return false;
    }

    private async Task ImportImageAsync()
    {
        var vm = ViewModel;
        if (vm == null || _imageAssetService == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("Image"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp", "*.tiff" }
                }
            }
        });

        if (files.Count == 0) return;

        var sourcePath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(sourcePath)) return;

        var result = await _imageAssetService.ImportAndCopyAsync(sourcePath, vm.Id);
        if (!result.IsSuccess)
            return;

        vm.Meta["imagePath"] = result.Value!;
        vm.NotifyContentChanged();

        await Dispatcher.UIThread.InvokeAsync(() => LoadBitmap(result.Value!));
    }

    // ── Flyout (Button.Flyout opens automatically — no extra Click handler) ───

    private async void FlyoutReplace_Click(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || _imageAssetService == null) return;

        var oldPath = GetMetaString(vm, "imagePath");

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("ImageFlyoutReplace"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp", "*.tiff" }
                }
            }
        });

        if (files.Count == 0) return;

        var sourcePath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(sourcePath)) return;

        var result = await _imageAssetService.ImportAndCopyAsync(sourcePath, vm.Id);
        if (!result.IsSuccess) return;

        if (!string.IsNullOrEmpty(oldPath) && oldPath != result.Value)
            await _imageAssetService.DeleteStoredFileAsync(oldPath);

        vm.Meta["imagePath"] = result.Value!;
        vm.NotifyContentChanged();

        await Dispatcher.UIThread.InvokeAsync(() => LoadBitmap(result.Value!));
    }

    private async void FlyoutCopy_Click(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        var imagePath = GetMetaString(vm, "imagePath");
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;

        var sp = (Application.Current as App)?.Services;
        var noteSvc = sp?.GetService(typeof(INoteClipboardPlatformService)) as INoteClipboardPlatformService;
        var codec = sp?.GetService(typeof(INoteClipboardPayloadCodec)) as INoteClipboardPayloadCodec;

        if (noteSvc != null && codec != null)
        {
            var list = new List<BlockViewModel> { vm };
            var markdown = BlockMarkdownSerializer.Serialize(list);
            var json = codec.Serialize(NoteClipboardMapper.ToDocument(list));
            Bitmap? bmp = null;
            try
            {
                bmp = new Bitmap(imagePath);
                await noteSvc.WriteAsync(topLevel.Clipboard, markdown, json, bmp).ConfigureAwait(true);
            }
            finally
            {
                bmp?.Dispose();
            }
            return;
        }

        Bitmap? fallback = null;
        try
        {
            fallback = new Bitmap(imagePath);
            await topLevel.Clipboard.SetBitmapAsync(fallback).ConfigureAwait(true);
        }
        finally
        {
            fallback?.Dispose();
        }
    }

    private void FlyoutDuplicate_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel?.RequestDuplicateBlock();
    }

    private void FlyoutDelete_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel?.NotifyStructuralChangeStarting();
        ViewModel?.RequestDelete();
    }

    // ── Caption ───────────────────────────────────────────────────────────────

    private void CaptionBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (ViewModel != null)
            ViewModel.IsFocused = true;
    }

    private void CaptionBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (CaptionBox?.IsFocused == true || HoverHost?.IsFocused == true)
                return;
            if (ViewModel == null) return;
            ViewModel.IsFocused = false;
            var editor = this.GetVisualAncestors().OfType<BlockEditor>().FirstOrDefault();
            editor?.FlushTypingBatch();
            editor?.NotifyBlocksChanged();
        }, DispatcherPriority.Input);
    }

    private void Caption_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        vm.Meta["imageAlt"] = CaptionBox.Text ?? string.Empty;
        vm.NotifyContentChanged();
        RefreshCaptionWatermark();
    }

    private void Caption_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Back || e.Handled) return;
        if (string.IsNullOrEmpty(CaptionBox.Text))
        {
            ViewModel?.NotifyStructuralChangeStarting();
            ViewModel?.RequestDeleteAndFocusAbove();
            e.Handled = true;
        }
    }

    // ── Resize handle (global pointer tracking while dragging) ────────────────

    private void ResizePill_PointerEntered(object? sender, PointerEventArgs e)
    {
        ResizePill.Opacity = 0.4;
    }

    private void ResizePill_PointerExited(object? sender, PointerEventArgs e)
    {
        if (!_isResizing)
            ResizePill.Opacity = 0.15;
    }

    private void ResizePill_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _isResizing = true;
        _resizeDragStartX = e.GetPosition(this).X;
        var maxW = GetMaxImageDisplayWidth();
        var currentWidth = DisplayImage.Width;
        var raw = !double.IsNaN(currentWidth) && currentWidth > 0
            ? currentWidth
            : (DisplayImage.Bounds.Width > 0 ? DisplayImage.Bounds.Width : 200);
        _resizeDragStartWidth = Math.Clamp(raw, 80, maxW);

        // We capture the outer border (sender) instead of the inner pill
        if (sender is InputElement element)
        {
            element.Cursor = new Cursor(StandardCursorType.SizeWestEast);
            e.Pointer.Capture(element);
        }
        e.Handled = true;
    }

    private void ResizeHitArea_PointerMoved(object? sender, PointerEventArgs e)
    {
        ResizeGlobal_PointerMoved(e);
    }

    private void ResizeHitArea_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ResizeGlobal_PointerReleased(e);
    }

    private void ResizeHitArea_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndResizeSession();
    }

    private void EndResizeSession()
    {
        if (!_isResizing) return;
        _isResizing = false;
        ResizePill.Opacity = 0.15;
    }

    private void ResizeGlobal_PointerMoved(PointerEventArgs e)
    {
        if (!_isResizing) return;

        var delta = e.GetPosition(this).X - _resizeDragStartX;
        var maxW = GetMaxImageDisplayWidth();
        var newWidth = Math.Clamp(_resizeDragStartWidth + delta, 80, maxW);

        DisplayImage.Width = newWidth;

        var vm = ViewModel;
        if (vm != null)
        {
            vm.Meta["imageWidth"] = newWidth;
            vm.NotifyContentChanged();
        }
    }

    private void ResizeGlobal_PointerReleased(PointerReleasedEventArgs e)
    {
        if (!_isResizing) return;

        e.Pointer.Capture(null);
        EndResizeSession();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateCaptionBox(string text)
    {
        if (CaptionBox != null && CaptionBox.Text != text)
            CaptionBox.Text = text;
        RefreshCaptionWatermark();
    }

    /// <summary>
    /// Max width for the block body (star column), from the list item row width — not the shrink-wrapped
    /// content chrome bounds (those match the current image width and would block scaling up).
    /// </summary>
    private double GetContentColumnConstraintWidth()
    {
        for (Visual? p = this.GetVisualParent(); p != null; p = p.GetVisualParent())
        {
            if (p is EditableBlock eb && eb.GetVisualParent() is Control slot && slot.Bounds.Width > 0)
                return Math.Max(0, slot.Bounds.Width - BlockItemContentChromeInset);

            if (p is ItemsControl ic && string.Equals(ic.Name, "BlocksItemsControl", StringComparison.Ordinal)
                && ic.Bounds.Width > 0)
                return Math.Max(0, ic.Bounds.Width - BlockItemContentChromeInset);

            if (p is BlockEditor be && be.Bounds.Width > 0)
            {
                const double editorHorizontalPadding = 64; // BlockEditor.axaml Border Padding 32,0,32,0
                return Math.Max(0, be.Bounds.Width - editorHorizontalPadding - BlockItemContentChromeInset);
            }
        }

        return 0;
    }

    private double GetMaxImageDisplayWidth()
    {
        double colW = GetContentColumnConstraintWidth();
        var innerPad = LoadedImageRow.IsVisible ? LoadedImageHitPadding * 2 : 0;
        var byViewport = colW > 0
            ? Math.Clamp(colW - innerPad, 80, MaxImageWidthCap)
            : MaxImageWidthCap;

        // When the image has a taller-than-wide aspect ratio, the MaxHeight="600" constraint
        // means the rendered width never reaches the full byViewport. Cap the drag accordingly.
        if (_currentBitmap != null)
        {
            var px = _currentBitmap.PixelSize;
            if (px.Height > 0 && px.Width > 0)
            {
                const double maxHeight = 600.0;
                var aspectCap = maxHeight * ((double)px.Width / px.Height);
                byViewport = Math.Min(byViewport, aspectCap);
            }
        }

        return byViewport;
    }

    private void ClampImageWidthToViewport()
    {
        if (DisplayImage == null || !LoadedImageRow.IsVisible) return;

        var maxW = GetMaxImageDisplayWidth();
        var w = DisplayImage.Width;

        if (double.IsNaN(w) || w <= 0)
        {
            var rendered = DisplayImage.Bounds.Width;
            if (rendered > maxW)
            {
                DisplayImage.Width = maxW;
                PersistClampedWidth(maxW);
            }
            return;
        }

        if (w > maxW)
        {
            DisplayImage.Width = maxW;
            PersistClampedWidth(maxW);
        }
    }

    private void PersistClampedWidth(double width)
    {
        var vm = ViewModel;
        if (vm == null) return;
        var cur = GetMetaDouble(vm, "imageWidth");
        if (Math.Abs(cur - width) < 0.5) return;
        vm.Meta["imageWidth"] = width;
        vm.NotifyContentChanged();
    }

    private void ApplyImageWidth(double width)
    {
        if (DisplayImage == null) return;
        var maxW = GetMaxImageDisplayWidth();
        if (width > 0)
            DisplayImage.Width = Math.Min(width, maxW);
        else
            DisplayImage.Width = double.NaN;
    }

    private static string GetMetaString(BlockViewModel vm, string key)
    {
        if (!vm.Meta.TryGetValue(key, out var val)) return string.Empty;
        if (val is string s) return s;
        if (val is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return val?.ToString() ?? string.Empty;
    }

    private static double GetMetaDouble(BlockViewModel vm, string key)
    {
        if (!vm.Meta.TryGetValue(key, out var val)) return 0;
        if (val is double d) return d;
        if (val is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetDouble();
        if (double.TryParse(val?.ToString(), out var parsed)) return parsed;
        return 0;
    }
}
