using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Mnemo.Core.Models.Widgets;
using Mnemo.UI.Modules.Overview.Controls;
using Mnemo.UI.Modules.Overview.ViewModels;

namespace Mnemo.UI.Modules.Overview.Views;

public partial class OverviewView : UserControl
{
    private TranslateTransform? _currentTransform;
    
    public OverviewView()
    {
        InitializeComponent();
    }

    private void OnWidgetDragStarted(object? sender, VectorEventArgs e)
    {
        if (sender is WidgetContainer container && 
            container.DataContext is DashboardWidgetViewModel widget &&
            DataContext is OverviewViewModel vm)
        {
            _currentTransform = new TranslateTransform();
            container.RenderTransform = _currentTransform;
            // Bring to front
            container.SetValue(Canvas.ZIndexProperty, 100);

            // Initial ghost update
            var currentPos = vm.GridToPixels(widget.Position);
            vm.UpdateGhostPosition(widget, currentPos.x, currentPos.y);
            vm.IsGhostVisible = true;
        }
    }

    private void OnWidgetDragDelta(object? sender, VectorEventArgs e)
    {
        if (sender is WidgetContainer container && 
            _currentTransform != null &&
            container.DataContext is DashboardWidgetViewModel widget &&
            DataContext is OverviewViewModel vm)
        {
            _currentTransform.X = e.Vector.X;
            _currentTransform.Y = e.Vector.Y;

            // Calculate new position relative to the grid
            var currentPos = vm.GridToPixels(widget.Position);
            var newX = currentPos.x + e.Vector.X;
            var newY = currentPos.y + e.Vector.Y;

            vm.UpdateGhostPosition(widget, newX, newY);
        }
    }

    private void OnWidgetDragCompleted(object? sender, VectorEventArgs e)
    {
        if (sender is WidgetContainer container &&
            container.DataContext is DashboardWidgetViewModel widget &&
            DataContext is OverviewViewModel vm)
        {
            // Find the ContentPresenter that hosts this item (so we can disable its position transition)
            var contentPresenter = container.FindAncestorOfType<ContentPresenter>();

            // Disable position transition so the widget doesn't snap back to start then animate to end
            Transitions? savedTransitions = null;
            if (contentPresenter != null)
            {
                savedTransitions = contentPresenter.Transitions;
                contentPresenter.Transitions = null;
            }

            // Reset ZIndex
            container.SetValue(Canvas.ZIndexProperty, 10);

            // Update position first (while transition is disabled), then clear transform
            if (vm.IsGhostVisible && vm.GhostWidget != null)
            {
                var newGridPos = vm.GhostWidget.Position;

                if (newGridPos.Column != widget.Position.Column || newGridPos.Row != widget.Position.Row)
                {
                    vm.TryMoveWidget(widget, newGridPos);
                }
            }

            container.RenderTransform = null;
            _currentTransform = null;

            // Restore transition for future layout changes
            if (contentPresenter != null && savedTransitions != null)
            {
                contentPresenter.Transitions = savedTransitions;
            }

            vm.IsGhostVisible = false;
            vm.GhostLeftPixels = 0;
            vm.GhostTopPixels = 0;
        }
    }

    private void OnWidgetRemoveRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is WidgetContainer container && container.Widget != null && DataContext is OverviewViewModel vm)
        {
            vm.RemoveWidget(container.Widget);
        }
    }
}