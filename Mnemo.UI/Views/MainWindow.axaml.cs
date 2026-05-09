using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services;
using Mnemo.UI.Components.RightSidebar;
using Mnemo.UI.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Views;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private bool _sidebarResizing;
    private Point _sidebarResizeStartPoint;
    private double _sidebarResizeStartWidth;
    private bool _isRightSidebarCollapsed = true;
    private INotifyPropertyChanged? _rightSidebarViewModelSubscribed;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDownTunnel, RoutingStrategies.Tunnel);
        Loaded += OnMainWindowLoaded;
    }

    private void OnMainWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnMainWindowLoaded;
        RegisterKeybindHandlers();
    }

    private void RegisterKeybindHandlers()
    {
        if (Application.Current is not App app || app.Services == null) return;
        if (DataContext is not MainWindowViewModel vm) return;
        var router = app.Services.GetRequiredService<IKeybindActionRouter>();
        router.RegisterHandler("global.search", () => vm.TopbarViewModel.OpenGlobalSearchCommand.Execute(null));
    }

    private void OnGlobalKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (Application.Current is not App app || app.Services == null) return;

        var keyMap = app.Services.GetRequiredService<IKeyMap>();
        var router = app.Services.GetRequiredService<IKeybindActionRouter>();
        var input = KeybindInputNormalizer.FromKeyEvent(e);
        var r = keyMap.ProcessGlobalKeyDown(input, DateTime.UtcNow, SequenceSwallowMode.SwallowOnPrefixAdvance);
        if (r.CompletedAction && !string.IsNullOrEmpty(r.ActionId))
            router.TryExecute(r.ActionId);
        if (r.Handled)
            e.Handled = true;
    }

    /// <summary>Exposed for content views (e.g. Overview) to bind padding to; avoids resolving DataContext.RightSidebarViewModel from XAML.</summary>
    public bool IsRightSidebarCollapsed
    {
        get => _isRightSidebarCollapsed;
        private set
        {
            if (_isRightSidebarCollapsed == value) return;
            _isRightSidebarCollapsed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRightSidebarCollapsed)));
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_rightSidebarViewModelSubscribed != null)
        {
            _rightSidebarViewModelSubscribed.PropertyChanged -= OnRightSidebarViewModelPropertyChanged;
            _rightSidebarViewModelSubscribed = null;
        }
        UpdateIsRightSidebarCollapsed();
        if (DataContext is MainWindowViewModel mainVm && mainVm.RightSidebarViewModel is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnRightSidebarViewModelPropertyChanged;
            _rightSidebarViewModelSubscribed = npc;
        }

        RegisterKeybindHandlers();
    }

    private void OnRightSidebarViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RightSidebarViewModel.IsCollapsed))
            UpdateIsRightSidebarCollapsed();
    }

    private void UpdateIsRightSidebarCollapsed()
    {
        IsRightSidebarCollapsed = (DataContext is MainWindowViewModel mainVm && mainVm.RightSidebarViewModel.IsCollapsed);
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void SidebarResizeHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || DataContext is not MainWindowViewModel mainVm)
            return;

        var root = this.VisualRoot as Visual;
        if (root == null)
            return;

        _sidebarResizing = true;
        _sidebarResizeStartPoint = e.GetPosition(root);
        _sidebarResizeStartWidth = mainVm.RightSidebarViewModel.ExpandedWidth;
        e.Pointer.Capture(sender as Control);
        e.Handled = true;
    }

    private void SidebarResizeHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_sidebarResizing || DataContext is not MainWindowViewModel mainVm)
            return;

        var root = this.VisualRoot as Visual;
        if (root == null)
            return;

        var current = e.GetPosition(root);
        var delta = _sidebarResizeStartPoint.X - current.X;
        var newWidth = Math.Clamp(_sidebarResizeStartWidth + delta, RightSidebarViewModel.MinWidth, RightSidebarViewModel.MaxWidth);
        mainVm.RightSidebarViewModel.ExpandedWidth = newWidth;
    }

    private void SidebarResizeHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasResizing = _sidebarResizing;
        if (_sidebarResizing)
        {
            _sidebarResizing = false;
            e.Pointer.Capture(null);
        }
        e.Handled = wasResizing;
    }

    private void SidebarResizeHandle_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _sidebarResizing = false;
    }
}
