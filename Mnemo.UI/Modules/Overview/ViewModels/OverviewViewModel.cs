using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Overview.Models;
using Mnemo.UI.Modules.Overview.Views;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Overview.ViewModels;

/// <summary>
/// ViewModel for the Overview dashboard.
/// Manages the grid layout, widget positions, and drag-and-drop behavior.
/// </summary>
public partial class OverviewViewModel : ViewModelBase
{
    private const string LayoutStorageKey = "overview_dashboard_layout";
    private const string UserDisplayNameKey = "User.DisplayName";
    private const string UserProfilePictureKey = "User.ProfilePicture";

    private readonly IWidgetRegistry _widgetRegistry;
    private readonly IOverlayService _overlayService;
    private readonly IStorageProvider _storage;
    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _logger;

    /// <summary>
    /// Gets the collection of active widgets on the dashboard.
    /// </summary>
    public ObservableCollection<DashboardWidgetViewModel> Widgets { get; } = new();

    /// <summary>
    /// Gets or sets whether edit mode is enabled (allows dragging).
    /// </summary>
    [ObservableProperty]
    private bool _isEditMode;

    /// <summary>
    /// Gets the ghost widget used for drag-and-drop feedback.
    /// </summary>
    [ObservableProperty]
    private DashboardWidgetViewModel? _ghostWidget;

    /// <summary>
    /// Gets or sets whether the ghost widget is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isGhostVisible;

    /// <summary>
    /// Gets or sets the ghost width in pixels (matches dragged widget size).
    /// </summary>
    [ObservableProperty]
    private double _ghostWidthPixels;

    /// <summary>
    /// Gets or sets the ghost height in pixels (matches dragged widget size).
    /// </summary>
    [ObservableProperty]
    private double _ghostHeightPixels;

    /// <summary>
    /// Gets or sets the ghost left position in pixels (avoids binding to GhostWidget when null).
    /// </summary>
    [ObservableProperty]
    private double _ghostLeftPixels;

    /// <summary>
    /// Gets or sets the ghost top position in pixels (avoids binding to GhostWidget when null).
    /// </summary>
    [ObservableProperty]
    private double _ghostTopPixels;

    /// <summary>
    /// Gets the calculated width for each grid cell.
    /// </summary>
    public int GridCellWidth => OverviewGridConstants.CellWidth;

    /// <summary>
    /// Gets the calculated height for each grid cell.
    /// </summary>
    public int GridCellHeight => OverviewGridConstants.CellHeight;

    /// <summary>
    /// Gets the spacing between grid cells.
    /// </summary>
    public int GridSpacing => OverviewGridConstants.CellSpacing;

    /// <summary>
    /// Gets the user's display name for the greeting.
    /// </summary>
    [ObservableProperty]
    private string _userName = string.Empty;

    /// <summary>
    /// Gets the user's profile picture path.
    /// </summary>
    [ObservableProperty]
    private string _profilePicturePath = "avares://Mnemo.UI/Assets/demo-profile-pic.png";

    /// <summary>
    /// Gets the name to show in the greeting (user name or "there" when empty), trimmed.
    /// </summary>
    public string GreetingName => string.IsNullOrWhiteSpace(UserName) ? "there" : UserName.Trim();

    public OverviewViewModel(IWidgetRegistry widgetRegistry, IOverlayService overlayService, IStorageProvider storage, ISettingsService settingsService, ILoggerService logger)
    {
        _widgetRegistry = widgetRegistry;
        _overlayService = overlayService;
        _storage = storage;
        _settingsService = settingsService;
        _logger = logger;

        _settingsService.SettingChanged += OnSettingChanged;
        RunAndLogAsync(LoadLayoutAsync(), "load dashboard layout");
        RunAndLogAsync(LoadUserProfileAsync(), "load user profile");
    }

    /// <summary>
    /// Runs an async operation without blocking; logs any exception at the boundary.
    /// </summary>
    private async void RunAndLogAsync(Task task, string context)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("Overview", $"Failed to {context}.", ex);
        }
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (key == UserDisplayNameKey || key == UserProfilePictureKey)
            _ = LoadUserProfileAsync();
    }

    private async Task LoadUserProfileAsync()
    {
        var name = await _settingsService.GetAsync(UserDisplayNameKey, string.Empty).ConfigureAwait(false);
        var pic = await _settingsService.GetAsync(UserProfilePictureKey, "avares://Mnemo.UI/Assets/demo-profile-pic.png").ConfigureAwait(false);
        
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            UserName = name ?? string.Empty;
            ProfilePicturePath = pic ?? "avares://Mnemo.UI/Assets/demo-profile-pic.png";
            OnPropertyChanged(nameof(GreetingName));
        });
    }

    partial void OnUserNameChanged(string value)
    {
        OnPropertyChanged(nameof(GreetingName));
    }

    /// <summary>
    /// Loads the dashboard layout from storage and restores widgets (or adds defaults if empty).
    /// </summary>
    private async Task LoadLayoutAsync()
    {
        var result = await _storage.LoadAsync<List<DashboardLayoutEntry>>(LayoutStorageKey).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (result.IsSuccess && result.Value is { Count: > 0 } entries)
            {
                foreach (var e in entries)
                {
                    await AddWidgetAsync(e.WidgetId, new WidgetPosition(e.Column, e.Row), new WidgetSize(e.ColSpan, e.RowSpan)).ConfigureAwait(false);
                }
            }
            else
            {
                await AddDefaultWidgetsAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds default widgets when no saved layout exists.
    /// </summary>
    private async Task AddDefaultWidgetsAsync()
    {
        await AddWidgetAsync("flashcard-stats", new WidgetPosition(0, 0)).ConfigureAwait(false);
        await AddWidgetAsync("recent-decks", new WidgetPosition(2, 0)).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists the current widget layout to storage.
    /// </summary>
    private async Task SaveLayoutAsync()
    {
        var entries = Widgets
            .Where(w => w.WidgetId != "ghost")
            .Select(w => new DashboardLayoutEntry(w.WidgetId, w.Position.Column, w.Position.Row, w.Size.ColSpan, w.Size.RowSpan))
            .ToList();
        await _storage.SaveAsync(LayoutStorageKey, entries).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a widget to the dashboard at the specified position and optional size (for restore; otherwise uses default).
    /// </summary>
    public async Task AddWidgetAsync(string widgetId, WidgetPosition position, WidgetSize? size = null)
    {
        var widget = _widgetRegistry.GetWidgetById(widgetId);
        if (widget == null)
            return;

        var widgetSize = size ?? widget.Metadata.DefaultSize;

        // Check if position is valid
        if (!IsPositionValid(position, widgetSize))
        {
            // Find next available position
            var availablePosition = FindNextAvailablePosition(widgetSize);
            if (availablePosition == null)
                return; // No space available

            position = availablePosition.Value;
        }

        var content = widget.CreateViewModel();
        var dashboardWidget = new DashboardWidgetViewModel(
            widgetId,
            widget.Metadata,
            content,
            position,
            widgetSize);

        dashboardWidget.IsEditMode = IsEditMode;
        Widgets.Add(dashboardWidget);
        await content.InitializeAsync().ConfigureAwait(false);
        RunAndLogAsync(SaveLayoutAsync(), "save dashboard layout");
    }

    /// <summary>
    /// Removes a widget from the dashboard.
    /// </summary>
    public void RemoveWidget(DashboardWidgetViewModel widget)
    {
        widget.Content.Dispose();
        Widgets.Remove(widget);
        RunAndLogAsync(SaveLayoutAsync(), "save dashboard layout");
    }

    /// <summary>
    /// Checks if a position is valid (no overlap and within bounds).
    /// </summary>
    public bool IsPositionValid(WidgetPosition position, WidgetSize size, DashboardWidgetViewModel? excludeWidget = null)
    {
        // Check bounds
        if (position.Column < 0 || position.Row < 0)
            return false;

        if (position.Column + size.ColSpan > OverviewGridConstants.GridColumns)
            return false;

        // Check overlap with other widgets
        foreach (var widget in Widgets)
        {
            if (widget == excludeWidget)
                continue;

            if (DoWidgetsOverlap(position, size, widget.Position, widget.Size))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Finds the next available position for a widget of the given size.
    /// </summary>
    public WidgetPosition? FindNextAvailablePosition(WidgetSize size)
    {
        // Simple algorithm: scan row by row, column by column
        for (int row = 0; row < 100; row++) // Arbitrary max rows
        {
            for (int col = 0; col <= OverviewGridConstants.GridColumns - size.ColSpan; col++)
            {
                var position = new WidgetPosition(col, row);
                if (IsPositionValid(position, size))
                    return position;
            }
        }

        return null;
    }

    /// <summary>
    /// Updates the ghost widget position based on drag coordinates.
    /// </summary>
    public void UpdateGhostPosition(DashboardWidgetViewModel draggedWidget, double x, double y)
    {
        var snappedPos = SnapToGrid(x, y);
        
        // Only update if position changed
        if (GhostWidget == null || !GhostWidget.Position.Equals(snappedPos))
        {
            // Update ghost position
            if (GhostWidget == null)
            {
                // Create a lightweight ghost if it doesn't exist (reuse the dragged widget's metadata/size)
                GhostWidget = new DashboardWidgetViewModel(
                    "ghost",
                    draggedWidget.Metadata,
                    null!, // No content needed for ghost
                    snappedPos,
                    draggedWidget.Size);
            }
            else
            {
                GhostWidget.Position = snappedPos;
            }

            // Keep ghost pixel size and position in sync (avoids binding to GhostWidget in XAML when null)
            GhostWidthPixels = draggedWidget.Size.ColSpan * OverviewGridConstants.CellWidth + (draggedWidget.Size.ColSpan - 1) * OverviewGridConstants.CellSpacing;
            GhostHeightPixels = draggedWidget.Size.RowSpan * OverviewGridConstants.CellHeight + (draggedWidget.Size.RowSpan - 1) * OverviewGridConstants.CellSpacing;
            GhostLeftPixels = snappedPos.Column * (OverviewGridConstants.CellWidth + OverviewGridConstants.CellSpacing);
            GhostTopPixels = snappedPos.Row * (OverviewGridConstants.CellHeight + OverviewGridConstants.CellSpacing);

            IsGhostVisible = IsPositionValid(snappedPos, draggedWidget.Size, draggedWidget);
        }
    }

    /// <summary>
    /// Snaps a pixel coordinate to the nearest grid position.
    /// </summary>
    public WidgetPosition SnapToGrid(double x, double y)
    {
        int col = (int)Math.Round(x / (OverviewGridConstants.CellWidth + OverviewGridConstants.CellSpacing));
        int row = (int)Math.Round(y / (OverviewGridConstants.CellHeight + OverviewGridConstants.CellSpacing));

        // Clamp to valid range
        col = Math.Max(0, Math.Min(col, OverviewGridConstants.GridColumns - 1));
        row = Math.Max(0, row);

        return new WidgetPosition(col, row);
    }

    /// <summary>
    /// Converts a grid position to pixel coordinates.
    /// </summary>
    public (double x, double y) GridToPixels(WidgetPosition position)
    {
        double x = position.Column * (OverviewGridConstants.CellWidth + OverviewGridConstants.CellSpacing);
        double y = position.Row * (OverviewGridConstants.CellHeight + OverviewGridConstants.CellSpacing);
        return (x, y);
    }

    /// <summary>
    /// Attempts to move a widget to a new position.
    /// Returns true if successful, false if the position is invalid.
    /// </summary>
    public bool TryMoveWidget(DashboardWidgetViewModel widget, WidgetPosition newPosition)
    {
        if (IsPositionValid(newPosition, widget.Size, widget))
        {
            widget.Position = newPosition;
            RunAndLogAsync(SaveLayoutAsync(), "save dashboard layout");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if two widgets overlap.
    /// </summary>
    private bool DoWidgetsOverlap(WidgetPosition pos1, WidgetSize size1, WidgetPosition pos2, WidgetSize size2)
    {
        int x1 = pos1.Column;
        int y1 = pos1.Row;
        int x2 = x1 + size1.ColSpan;
        int y2 = y1 + size1.RowSpan;

        int x3 = pos2.Column;
        int y3 = pos2.Row;
        int x4 = x3 + size2.ColSpan;
        int y4 = y3 + size2.RowSpan;

        // Check if rectangles overlap
        return !(x2 <= x3 || x4 <= x1 || y2 <= y3 || y4 <= y1);
    }

    [RelayCommand]
    private void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
        SyncEditModeToWidgets();
    }

    private void SyncEditModeToWidgets()
    {
        foreach (var w in Widgets)
            w.IsEditMode = IsEditMode;
    }

    [RelayCommand]
    private void AddWidget()
    {
        var vm = new AddWidgetViewModel(_widgetRegistry, _overlayService, this);
        var view = new AddWidgetView { DataContext = vm };
        
        var options = new OverlayOptions
        {
             ShowBackdrop = true,
             CloseOnOutsideClick = true,
             HorizontalAlignment = "Center",
             VerticalAlignment = "Center"
        };
        
        var id = _overlayService.CreateOverlay(view, options);
        vm.OverlayId = id;
    }
}
