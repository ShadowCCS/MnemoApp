using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;

namespace Mnemo.UI.Modules.Overview.ViewModels;

/// <summary>
/// ViewModel for the Add Widget overlay.
/// Shows available widgets and adds the selected one to the dashboard at the first available position.
/// </summary>
public partial class AddWidgetViewModel : ObservableObject
{
    private readonly IWidgetRegistry _widgetRegistry;
    private readonly IOverlayService _overlayService;
    private readonly OverviewViewModel _overviewViewModel;

    /// <summary>
    /// Gets or sets the overlay identifier used to close this overlay.
    /// </summary>
    public string OverlayId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the list of available widget metadata for the gallery.
    /// </summary>
    public ObservableCollection<WidgetMetadata> AvailableWidgets { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AddWidgetViewModel"/> class.
    /// </summary>
    /// <param name="widgetRegistry">Registry of available widgets.</param>
    /// <param name="overlayService">Service to close the overlay.</param>
    /// <param name="overviewViewModel">Overview dashboard ViewModel to add the widget to.</param>
    public AddWidgetViewModel(
        IWidgetRegistry widgetRegistry,
        IOverlayService overlayService,
        OverviewViewModel overviewViewModel)
    {
        _widgetRegistry = widgetRegistry;
        _overlayService = overlayService;
        _overviewViewModel = overviewViewModel;

        LoadWidgets();
    }

    private void LoadWidgets()
    {
        var widgets = _widgetRegistry.GetAllWidgets();
        foreach (var widget in widgets)
        {
            AvailableWidgets.Add(widget.Metadata);
        }
    }

    /// <summary>
    /// Adds the selected widget to the dashboard at the first available position and closes the overlay.
    /// </summary>
    /// <param name="widget">Metadata of the widget to add; ignored if null.</param>
    [RelayCommand]
    private async Task AddWidget(WidgetMetadata? widget)
    {
        if (widget == null)
            return;

        await _overviewViewModel.AddWidgetAsync(widget.Id, new WidgetPosition(0, 0));
        _overlayService.CloseOverlay(OverlayId);
    }

    /// <summary>
    /// Cancels adding a widget and closes the overlay.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _overlayService.CloseOverlay(OverlayId);
    }
}
