using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace Mnemo.UI.Controls;

public partial class SegmentedToggle : UserControl
{
    public static readonly StyledProperty<string> LeftTextProperty =
        AvaloniaProperty.Register<SegmentedToggle, string>(nameof(LeftText), "Left");

    public static readonly StyledProperty<string> RightTextProperty =
        AvaloniaProperty.Register<SegmentedToggle, string>(nameof(RightText), "Right");

    public static readonly StyledProperty<string> LeftIconPathProperty =
        AvaloniaProperty.Register<SegmentedToggle, string>(nameof(LeftIconPath), string.Empty);

    public static readonly StyledProperty<string> RightIconPathProperty =
        AvaloniaProperty.Register<SegmentedToggle, string>(nameof(RightIconPath), string.Empty);

    public static readonly StyledProperty<bool> IsLeftSelectedProperty =
        AvaloniaProperty.Register<SegmentedToggle, bool>(nameof(IsLeftSelected), true);

    public static readonly StyledProperty<ICommand?> LeftCommandProperty =
        AvaloniaProperty.Register<SegmentedToggle, ICommand?>(nameof(LeftCommand));

    public static readonly StyledProperty<ICommand?> RightCommandProperty =
        AvaloniaProperty.Register<SegmentedToggle, ICommand?>(nameof(RightCommand));

    public static readonly StyledProperty<IBrush?> SelectedForegroundBrushProperty =
        AvaloniaProperty.Register<SegmentedToggle, IBrush?>(nameof(SelectedForegroundBrush));

    public static readonly StyledProperty<IBrush?> UnselectedForegroundBrushProperty =
        AvaloniaProperty.Register<SegmentedToggle, IBrush?>(nameof(UnselectedForegroundBrush));

    public static readonly DirectProperty<SegmentedToggle, int> SelectedIndexProperty =
        AvaloniaProperty.RegisterDirect<SegmentedToggle, int>(
            nameof(SelectedIndex),
            o => o.SelectedIndex);

    public static readonly DirectProperty<SegmentedToggle, IBrush> LeftForegroundProperty =
        AvaloniaProperty.RegisterDirect<SegmentedToggle, IBrush>(
            nameof(LeftForeground),
            o => o.LeftForeground);

    public static readonly DirectProperty<SegmentedToggle, IBrush> RightForegroundProperty =
        AvaloniaProperty.RegisterDirect<SegmentedToggle, IBrush>(
            nameof(RightForeground),
            o => o.RightForeground);

    private int _selectedIndex;
    private IBrush _leftForeground = Brushes.White;
    private IBrush _rightForeground = Brushes.Gray;

    public int SelectedIndex => _selectedIndex;

    public IBrush LeftForeground => _leftForeground;

    public IBrush RightForeground => _rightForeground;

    static SegmentedToggle()
    {
        IsLeftSelectedProperty.Changed.AddClassHandler<SegmentedToggle>((x, _) =>
        {
            x.UpdateComputedState();
        });
        SelectedForegroundBrushProperty.Changed.AddClassHandler<SegmentedToggle>((x, _) =>
        {
            x.UpdateComputedState();
        });
        UnselectedForegroundBrushProperty.Changed.AddClassHandler<SegmentedToggle>((x, _) =>
        {
            x.UpdateComputedState();
        });
    }

    public SegmentedToggle()
    {
        InitializeComponent();
        UpdateComputedState();
    }

    public string LeftText { get => GetValue(LeftTextProperty); set => SetValue(LeftTextProperty, value); }
    public string RightText { get => GetValue(RightTextProperty); set => SetValue(RightTextProperty, value); }

    public string LeftIconPath { get => GetValue(LeftIconPathProperty); set => SetValue(LeftIconPathProperty, value); }
    public string RightIconPath { get => GetValue(RightIconPathProperty); set => SetValue(RightIconPathProperty, value); }

    public bool IsLeftSelected { get => GetValue(IsLeftSelectedProperty); set => SetValue(IsLeftSelectedProperty, value); }

    public IBrush? SelectedForegroundBrush
    {
        get => GetValue(SelectedForegroundBrushProperty);
        set => SetValue(SelectedForegroundBrushProperty, value);
    }

    public IBrush? UnselectedForegroundBrush
    {
        get => GetValue(UnselectedForegroundBrushProperty);
        set => SetValue(UnselectedForegroundBrushProperty, value);
    }

    public ICommand? LeftCommand { get => GetValue(LeftCommandProperty); set => SetValue(LeftCommandProperty, value); }
    public ICommand? RightCommand { get => GetValue(RightCommandProperty); set => SetValue(RightCommandProperty, value); }

    private void OnLeftClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IsLeftSelected = true;
    }

    private void OnRightClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IsLeftSelected = false;
    }

    private void UpdateComputedState()
    {
        var selectedIndex = IsLeftSelected ? 0 : 1;
        var selectedForeground = SelectedForegroundBrush ?? Brushes.White;
        var unselectedForeground = UnselectedForegroundBrush ?? Brushes.Gray;
        var leftForeground = IsLeftSelected ? selectedForeground : unselectedForeground;
        var rightForeground = IsLeftSelected ? unselectedForeground : selectedForeground;

        SetAndRaise(SelectedIndexProperty, ref _selectedIndex, selectedIndex);
        SetAndRaise(LeftForegroundProperty, ref _leftForeground, leftForeground);
        SetAndRaise(RightForegroundProperty, ref _rightForeground, rightForeground);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}