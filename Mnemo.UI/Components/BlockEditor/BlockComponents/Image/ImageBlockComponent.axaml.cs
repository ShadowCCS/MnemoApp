using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
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
    private TopLevel? _resizeTopLevel;
    private EventHandler<PointerEventArgs>? _resizePointerMoved;
    private EventHandler<PointerReleasedEventArgs>? _resizePointerReleased;

    /// <summary>Space reserved for resize hit column + margins so the pill stays in view.</summary>
    private const double ResizeColumnReserve = 32;

    /// <summary>Hard cap so huge monitors do not allow absurd image widths.</summary>
    private const double MaxImageWidthCap = 1600;

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
        UnsubscribeResizeTopLevel();
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
        SetPh(FlyoutCaptionItem, "ImageFlyoutCaption");
        SetPh(FlyoutDuplicateItem, "ImageFlyoutDuplicate");
        SetPh(FlyoutDeleteItem, "ImageFlyoutDelete");
        SetPh(PhFlyoutReplaceItem, "ImageFlyoutReplace");
        SetPh(PhFlyoutCopyItem, "ImageFlyoutCopyImage");
        SetPh(PhFlyoutCaptionItem, "ImageFlyoutCaption");
        SetPh(PhFlyoutDuplicateItem, "ImageFlyoutDuplicate");
        SetPh(PhFlyoutDeleteItem, "ImageFlyoutDelete");
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
        FlyoutButton.IsVisible = false;
        FlyoutButtonPlaceholder.IsVisible = false;
        UpdateCaptionHostWidth();
    }

    private void ShowImageArea()
    {
        PlaceholderBorder.IsVisible = false;
        LoadedImageRow.IsVisible = true;
        FlyoutButtonPlaceholder.IsVisible = false;
        UpdateCaptionHostWidth();
    }

    private void UpdateCaptionHostWidth()
    {
        if (CaptionHost == null) return;
        if (LoadedImageRow.IsVisible && DisplayImage != null && DisplayImage.Bounds.Width > 0)
        {
            CaptionHost.Width = DisplayImage.Bounds.Width;
            CaptionHost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        }
        else
        {
            CaptionHost.Width = double.NaN;
            CaptionHost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        }
    }

    // ── Hover host (menu visibility + keyboard focus) ─────────────────────────

    private void HoverHost_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (LoadedImageRow.IsVisible)
        {
            FlyoutButton.IsVisible = true;
            FlyoutButton.Opacity = 1;
        }
        if (PlaceholderBorder.IsVisible)
        {
            FlyoutButtonPlaceholder.IsVisible = true;
            FlyoutButtonPlaceholder.Opacity = 1;
        }
        RefreshCaptionWatermark();
    }

    private void HoverHost_PointerExited(object? sender, PointerEventArgs e)
    {
        if (FlyoutButton.Flyout?.IsOpen == true || FlyoutButtonPlaceholder.Flyout?.IsOpen == true)
            return;

        FlyoutButton.Opacity = 0;
        FlyoutButton.IsVisible = false;
        FlyoutButtonPlaceholder.Opacity = 0;
        FlyoutButtonPlaceholder.IsVisible = false;
        RefreshCaptionWatermark();
    }

    private void HoverHost_PointerPressed(object? sender, PointerPressedEventArgs e)
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
        e.Handled = true;
        await ImportImageAsync();
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

    private void FlyoutCaption_Click(object? sender, RoutedEventArgs e)
    {
        CaptionBox.Focus();
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

        SubscribeResizeTopLevel();
    }

    private void SubscribeResizeTopLevel()
    {
        UnsubscribeResizeTopLevel();
        _resizeTopLevel = TopLevel.GetTopLevel(this);
        if (_resizeTopLevel == null) return;

        _resizePointerMoved = (_, ev) => ResizeGlobal_PointerMoved(ev);
        _resizePointerReleased = (_, ev) => ResizeGlobal_PointerReleased(ev);
        _resizeTopLevel.AddHandler(InputElement.PointerMovedEvent, _resizePointerMoved, RoutingStrategies.Tunnel);
        _resizeTopLevel.AddHandler(InputElement.PointerReleasedEvent, _resizePointerReleased, RoutingStrategies.Tunnel);
    }

    private void UnsubscribeResizeTopLevel()
    {
        if (_resizeTopLevel != null)
        {
            if (_resizePointerMoved != null)
                _resizeTopLevel.RemoveHandler(InputElement.PointerMovedEvent, _resizePointerMoved);
            if (_resizePointerReleased != null)
                _resizeTopLevel.RemoveHandler(InputElement.PointerReleasedEvent, _resizePointerReleased);
        }
        _resizeTopLevel = null;
        _resizePointerMoved = null;
        _resizePointerReleased = null;
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

        _isResizing = false;
        ResizePill.Opacity = 0.15;
        e.Pointer.Capture(null);
        UnsubscribeResizeTopLevel();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateCaptionBox(string text)
    {
        if (CaptionBox != null && CaptionBox.Text != text)
            CaptionBox.Text = text;
        RefreshCaptionWatermark();
    }

    private double GetMaxImageDisplayWidth()
    {
        double avail = HoverHost?.Bounds.Width ?? 0;
        if (avail <= 0)
            avail = Bounds.Width;
        if (avail <= 0)
            return MaxImageWidthCap;
        var byViewport = avail - ResizeColumnReserve;
        return Math.Clamp(byViewport, 80, MaxImageWidthCap);
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
