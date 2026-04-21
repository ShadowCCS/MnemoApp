using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Mnemo.UI.Components;

/// <summary>
/// A premium, smoothly animated collapsible section used for grouping related
/// settings (or any other content). The header toggles expansion; the body
/// slides open with a max-height+opacity transition while the chevron rotates.
/// </summary>
public class CollapsibleSection : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<CollapsibleSection, string?>(nameof(Title));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<CollapsibleSection, bool>(
            nameof(IsExpanded),
            defaultValue: true,
            defaultBindingMode: BindingMode.TwoWay);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    private const string PART_HeaderButton = "PART_HeaderButton";
    private const string PART_ContentHost = "PART_ContentHost";
    private const string PART_ContentPresenter = "PART_ContentPresenter";
    private const double ExpandedMaxHeight = 10000d;

    private Button? _headerButton;
    private Border? _contentHost;
    private ContentPresenter? _contentPresenter;
    private bool _templateApplied;

    static CollapsibleSection()
    {
        IsExpandedProperty.Changed.AddClassHandler<CollapsibleSection>((x, _) => x.OnExpansionChanged());
    }

    public CollapsibleSection()
    {
        UpdatePseudoClasses();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_headerButton != null)
            _headerButton.Click -= OnHeaderClick;
        _headerButton = e.NameScope.Find<Button>(PART_HeaderButton);
        _contentHost = e.NameScope.Find<Border>(PART_ContentHost);
        _contentPresenter = e.NameScope.Find<ContentPresenter>(PART_ContentPresenter);

        if (_headerButton != null)
            _headerButton.Click += OnHeaderClick;

        _templateApplied = true;
        ApplyState(animate: false);
    }

    private void OnHeaderClick(object? sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
    }

    private void OnExpansionChanged()
    {
        UpdatePseudoClasses();
        ApplyState(animate: true);
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":collapsed", !IsExpanded);
    }

    private void ApplyState(bool animate)
    {
        if (!_templateApplied || _contentHost is null)
            return;

        if (!animate)
        {
            var savedTransitions = _contentHost.Transitions;
            _contentHost.Transitions = null;
            if (IsExpanded)
            {
                _contentHost.MaxHeight = ExpandedMaxHeight;
                _contentHost.Opacity = 1;
            }
            else
            {
                _contentHost.MaxHeight = 0;
                _contentHost.Opacity = 0;
            }
            _contentHost.Transitions = savedTransitions;
            return;
        }

        if (IsExpanded)
        {
            _contentHost.MaxHeight = ExpandedMaxHeight;
            _contentHost.Opacity = 1;
        }
        else
        {
            _contentHost.MaxHeight = 0;
            _contentHost.Opacity = 0;
        }
    }
}
