using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Mnemo.UI.Modules.Flashcards.ViewModels;
using System;
using System.ComponentModel;

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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(PointerMovedEvent, OnRootPointerMoved);
        RemoveHandler(PointerReleasedEvent, OnRootPointerReleased);
        _dragCoordinator?.Dispose();
        _dragCoordinator = null;
        base.OnDetachedFromVisualTree(e);
    }
}
