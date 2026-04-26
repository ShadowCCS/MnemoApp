using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Flashcards;
using Mnemo.UI.Modules.Flashcards.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.Components.Overlays;

namespace Mnemo.UI.Modules.Flashcards.Views;

public partial class FlashcardsView : UserControl, INotifyPropertyChanged
{
    private const double DragStartThreshold = 5.0;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public IRelayCommand<FlashcardDeckRowViewModel?>? StartQuickSessionCommandProxy => (DataContext as FlashcardsViewModel)?.StartQuickSessionCommand;
    public IRelayCommand<FlashcardDeckRowViewModel?>? OpenDeckCommandProxy => (DataContext as FlashcardsViewModel)?.OpenDeckCommand;
    public IAsyncRelayCommand<FlashcardDeckRowViewModel?>? OpenDeckSettingsCommandProxy => (DataContext as FlashcardsViewModel)?.OpenDeckSettingsCommand;
    public IAsyncRelayCommand<FlashcardDeckRowViewModel?>? DeleteDeckCommandProxy => (DataContext as FlashcardsViewModel)?.DeleteDeckCommand;

    private Border? _dragArmedDeckBorder;
    private Point _dragArmedDeckPoint;
    internal FlashcardsDragCoordinator? _dragCoordinator;

    public FlashcardsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(PointerMovedEvent, OnRootPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnRootPointerReleased, RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartQuickSessionCommandProxy)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpenDeckCommandProxy)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpenDeckSettingsCommandProxy)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeleteDeckCommandProxy)));
    }

    private void OnDeckCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || DataContext is not FlashcardsViewModel)
            return;

        if (border.DataContext is not FlashcardDeckRowViewModel row)
            return;

        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is StyledElement source)
        {
            StyledElement? current = source;
            while (current is not null)
            {
                if (current is Button)
                    return;
                current = current.Parent as StyledElement;
            }
        }

        _dragArmedDeckBorder = border;
        _dragArmedDeckPoint = e.GetPosition(border);
        e.Pointer.Capture(border);

        e.Handled = true;
    }

    private void OnDeckCardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Border border)
            return;
        if (!ReferenceEquals(_dragArmedDeckBorder, border))
            return;
        if (border.DataContext is not FlashcardDeckRowViewModel row || string.IsNullOrWhiteSpace(row.Id))
            return;
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            return;

        var current = e.GetPosition(border);
        var delta = current - _dragArmedDeckPoint;
        if (Math.Abs(delta.X) < DragStartThreshold && Math.Abs(delta.Y) < DragStartThreshold)
            return;

        e.Pointer.Capture(null);
        _dragArmedDeckBorder = null;
        EnsureDragCoordinator();
        _dragCoordinator?.BeginDeckDrag(row, border, e.Pointer);
    }

    private void OnDeckCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Border border || DataContext is not FlashcardsViewModel vm)
            return;

        e.Pointer.Capture(null);
        if (!ReferenceEquals(_dragArmedDeckBorder, border))
            return;

        _dragArmedDeckBorder = null;

        if (border.DataContext is not FlashcardDeckRowViewModel row)
            return;
        if (e.Source is StyledElement source && IsEventFromButton(source))
            return;

        if (vm.OpenDeckCommand.CanExecute(row))
            vm.OpenDeckCommand.Execute(row);
        e.Handled = true;
    }

    private static bool IsEventFromButton(StyledElement source)
    {
        StyledElement? current = source;
        while (current is not null)
        {
            if (current is Button)
                return true;
            current = current.Parent as StyledElement;
        }

        return false;
    }

    private void EnsureDragCoordinator()
    {
        if (_dragCoordinator != null)
            return;

        if (this.FindControl<Canvas>("DragOverlayCanvas") is not Canvas overlay)
            return;

        _dragCoordinator = new FlashcardsDragCoordinator(overlay, this);
    }

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCoordinator?.IsDragging != true)
            return;

        _dragCoordinator.OnPointerMoved(e);
    }

    private async void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragCoordinator?.IsDragging != true || DataContext is not FlashcardsViewModel vm)
            return;

        var drop = _dragCoordinator.CompleteDrag(e.Pointer);
        if (drop is null)
            return;

        if (drop.Value.SourceKind == FlashcardsDragCoordinator.DragSourceKind.Deck &&
            !string.IsNullOrWhiteSpace(drop.Value.TargetFolderId))
        {
            await vm.MoveDeckToFolderAsync(drop.Value.SourceId, drop.Value.TargetFolderId);
            return;
        }

        if (drop.Value.SourceKind == FlashcardsDragCoordinator.DragSourceKind.Folder &&
            !string.IsNullOrWhiteSpace(drop.Value.TargetFolderId) &&
            drop.Value.FolderMode != FlashcardsDragCoordinator.FolderDropMode.None)
        {
            await vm.MoveFolderAsync(
                drop.Value.SourceId,
                drop.Value.TargetFolderId,
                drop.Value.FolderMode == FlashcardsDragCoordinator.FolderDropMode.DropIntoFolder,
                drop.Value.FolderMode == FlashcardsDragCoordinator.FolderDropMode.InsertBelow);
        }
    }

    public void InitiateFolderDrag(FlashcardFolderItemViewModel item, FlashcardFolderRow row, IPointer pointer)
    {
        EnsureDragCoordinator();
        _dragCoordinator?.BeginFolderDrag(item, row, pointer);
    }

    private async void OnTransferClick(object? sender, RoutedEventArgs e)
    {
        var app = Application.Current as App;
        var services = app?.Services;
        if (services == null || DataContext is not FlashcardsViewModel vm)
            return;

        var coordinator = services.GetService<IImportExportCoordinator>();
        var overlayService = services.GetService<IOverlayService>();
        if (coordinator == null || overlayService == null)
            return;

        var button = sender as Button;
        var startTransfer = string.Equals(button?.Tag?.ToString(), "transfer", StringComparison.OrdinalIgnoreCase);
        var capabilities = coordinator.GetCapabilities("flashcards");
        var overlay = new TransferOverlay
        {
            Title = "Transfer Flashcards",
            Description = "Choose format and settings."
        };
        overlay.Initialize(capabilities, startTransfer);

        var overlayId = overlayService.CreateOverlay(overlay, new OverlayOptions
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            ShowBackdrop = true,
            CloseOnOutsideClick = true
        }, "TransferOverlay");

        var tcs = new TaskCompletionSource<TransferOverlayResult?>();
        overlay.OnResult = result =>
        {
            overlayService.CloseOverlay(overlayId);
            tcs.TrySetResult(result);
        };
        var selected = await tcs.Task.ConfigureAwait(true);
        if (selected == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
            return;

        if (selected.IsImport)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Import flashcards",
                FileTypeFilter = [new FilePickerFileType(selected.Format.DisplayName) { Patterns = selected.Format.Extensions.Select(ext => $"*{ext}").ToArray() }]
            });
            var file = files.FirstOrDefault();
            if (file == null)
                return;

            var result = await coordinator.ImportAsync(new ImportExportRequest
            {
                ContentType = "flashcards",
                FormatId = selected.Format.FormatId,
                FilePath = file.Path.LocalPath
            }).ConfigureAwait(true);

            var importSucceeded = result.IsSuccess && result.Value is { Success: true };
            var importedDecks = result.Value?.ProcessedCounts.TryGetValue("decks", out var deckCount) == true ? deckCount : 0;
            var importedCards = result.Value?.ProcessedCounts.TryGetValue("flashcards", out var cardCount) == true ? cardCount : 0;
            var importMessage = importSucceeded
                ? $"Flashcards import finished. Imported {importedDecks} deck(s), {importedCards} card(s)."
                : result.Value?.ErrorMessage ?? result.ErrorMessage ?? "Import failed.";

            await overlayService.CreateDialogAsync(
                importSucceeded ? "Import complete" : "Import failed",
                importMessage).ConfigureAwait(true);

            if (importSucceeded)
            {
                vm.SelectAllDecks();
                await vm.RefreshCommand.ExecuteAsync(null);
            }
            return;
        }

        var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export flashcards",
            SuggestedFileName = $"flashcards{selected.Format.Extensions.FirstOrDefault() ?? ".mnemo"}",
            DefaultExtension = selected.Format.Extensions.FirstOrDefault()?.TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(selected.Format.DisplayName) { Patterns = selected.Format.Extensions.Select(ext => $"*{ext}").ToArray() }]
        });
        if (saveFile == null)
            return;

        object? payload = null;
        var filteredDeckIds = vm.FilteredDecks
            .Where(deck => !string.IsNullOrWhiteSpace(deck.Id))
            .Select(deck => deck.Id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (filteredDeckIds.Length == 0)
        {
            await overlayService.CreateDialogAsync("Export failed", "No decks are currently visible to export.").ConfigureAwait(true);
            return;
        }
        if (filteredDeckIds.Length > 0)
            payload = filteredDeckIds;

        var export = await coordinator.ExportAsync(new ImportExportRequest
        {
            ContentType = "flashcards",
            FormatId = selected.Format.FormatId,
            FilePath = saveFile.Path.LocalPath,
            Payload = payload
        }).ConfigureAwait(true);

        var exportSucceeded = export.IsSuccess && export.Value is { Success: true };
        var exportMessage = exportSucceeded
            ? "Flashcards export finished."
            : export.Value?.ErrorMessage ?? export.ErrorMessage ?? "Export failed.";
        await overlayService.CreateDialogAsync(exportSucceeded ? "Export complete" : "Export failed", exportMessage).ConfigureAwait(true);
    }

    private async void OnDeckRenameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: FlashcardDeckRowViewModel row })
            return;
        var app = Application.Current as App;
        var services = app?.Services;
        if (services == null || DataContext is not FlashcardsViewModel vm)
            return;
        var overlayService = services.GetService<IOverlayService>();
        var deckService = services.GetService<IFlashcardDeckService>();
        if (overlayService == null || deckService == null)
            return;

        var input = new InputDialogOverlay
        {
            Title = "Rename deck",
            Placeholder = "Deck name",
            InputValue = row.Name,
            ConfirmText = "Save",
            CancelText = "Cancel"
        };
        var id = overlayService.CreateOverlay(input, new OverlayOptions { ShowBackdrop = true, CloseOnOutsideClick = true });
        var tcs = new TaskCompletionSource<string?>();
        input.OnResult = value =>
        {
            overlayService.CloseOverlay(id);
            tcs.TrySetResult(value);
        };
        var newName = (await tcs.Task.ConfigureAwait(true) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, row.Name, StringComparison.Ordinal))
            return;
        var deck = await deckService.GetDeckByIdAsync(row.Id).ConfigureAwait(true);
        if (deck == null)
            return;
        await deckService.SaveDeckAsync(deck with { Name = newName }).ConfigureAwait(true);
        await vm.RefreshCommand.ExecuteAsync(null);
    }

    private async void OnDeckDuplicateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: FlashcardDeckRowViewModel row })
            return;
        var app = Application.Current as App;
        var services = app?.Services;
        if (services == null || DataContext is not FlashcardsViewModel vm)
            return;
        var deckService = services.GetService<IFlashcardDeckService>();
        if (deckService == null)
            return;
        var deck = await deckService.GetDeckByIdAsync(row.Id).ConfigureAwait(true);
        if (deck == null)
            return;
        var copy = deck with
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = $"{deck.Name} Copy",
            Cards = deck.Cards.Select(card => card with { Id = Guid.NewGuid().ToString("n") }).ToArray(),
            LastStudied = null
        };
        await deckService.SaveDeckAsync(copy).ConfigureAwait(true);
        await vm.RefreshCommand.ExecuteAsync(null);
    }

    private async void OnDeckExportClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: FlashcardDeckRowViewModel row })
            return;
        var app = Application.Current as App;
        var services = app?.Services;
        if (services == null || DataContext is not FlashcardsViewModel vm)
            return;
        var coordinator = services.GetService<IImportExportCoordinator>();
        var overlayService = services.GetService<IOverlayService>();
        var deckService = services.GetService<IFlashcardDeckService>();
        if (coordinator == null || overlayService == null || deckService == null)
            return;
        var deck = await deckService.GetDeckByIdAsync(row.Id).ConfigureAwait(true);
        if (deck == null)
            return;

        var capabilities = coordinator.GetCapabilities("flashcards").Where(c => c.SupportsExport).ToArray();
        var overlay = new TransferOverlay { Title = "Export Deck", Description = "Choose format and settings.", ConfirmText = "Export" };
        overlay.Initialize(capabilities, defaultImport: false);
        var overlayId = overlayService.CreateOverlay(overlay, new OverlayOptions
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            ShowBackdrop = true,
            CloseOnOutsideClick = true
        }, "TransferOverlay");
        var tcs = new TaskCompletionSource<TransferOverlayResult?>();
        overlay.OnResult = result =>
        {
            overlayService.CloseOverlay(overlayId);
            tcs.TrySetResult(result);
        };
        var selected = await tcs.Task.ConfigureAwait(true);
        if (selected == null)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
            return;
        var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export deck",
            SuggestedFileName = $"{SanitizeFileName(deck.Name)}{selected.Format.Extensions.FirstOrDefault() ?? ".mnemo"}",
            DefaultExtension = selected.Format.Extensions.FirstOrDefault()?.TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(selected.Format.DisplayName) { Patterns = selected.Format.Extensions.Select(ext => $"*{ext}").ToArray() }]
        });
        if (saveFile == null)
            return;
        object? payload = selected.Format.FormatId == "flashcards.csv" ? deck : deck.Id;
        var export = await coordinator.ExportAsync(new ImportExportRequest
        {
            ContentType = "flashcards",
            FormatId = selected.Format.FormatId,
            FilePath = saveFile.Path.LocalPath,
            Payload = payload
        }).ConfigureAwait(true);
        var exportSucceeded = export.IsSuccess && export.Value is { Success: true };
        var exportMessage = exportSucceeded
            ? "Deck export finished."
            : export.Value?.ErrorMessage ?? export.ErrorMessage ?? "Export failed.";
        await overlayService.CreateDialogAsync(exportSucceeded ? "Export complete" : "Export failed", exportMessage).ConfigureAwait(true);
        await vm.RefreshCommand.ExecuteAsync(null);
    }

    private static string SanitizeFileName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "flashcards" : value.Trim();
        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return name;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(PointerMovedEvent, OnRootPointerMoved);
        RemoveHandler(PointerReleasedEvent, OnRootPointerReleased);
        _dragCoordinator?.Dispose();
        _dragCoordinator = null;
        base.OnDetachedFromVisualTree(e);
    }
}
