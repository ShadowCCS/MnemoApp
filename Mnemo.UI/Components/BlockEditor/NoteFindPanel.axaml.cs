using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Mnemo.UI.Components.BlockEditor;

public partial class NoteFindPanel : UserControl
{
    public NoteFindPanel()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnRootTunnelKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Host receives find/replace UI events (overlay is not in the block editor visual tree).</summary>
    public BlockEditor? EditorHost { get; set; }

    private void OnRootTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || (e.KeyModifiers & KeyModifiers.Control) == 0)
            return;
        FindQueryTextBox.Focus();
        FindQueryTextBox.SelectAll();
        e.Handled = true;
    }

    private void FindQueryTextBox_OnTextChanged(object? sender, TextChangedEventArgs e) =>
        EditorHost?.OnNoteFindPanelFindQueryTextChanged(sender, e);

    private void ReplaceQueryTextBox_OnTextChanged(object? sender, TextChangedEventArgs e) =>
        EditorHost?.OnNoteFindPanelReplaceQueryTextChanged(sender, e);

    private void FindOptionCheckBox_OnChanged(object? sender, RoutedEventArgs e) =>
        EditorHost?.OnNoteFindPanelOptionChanged(sender, e);

    private void FindPreviousButton_OnClick(object? sender, RoutedEventArgs e) =>
        EditorHost?.OnNoteFindPanelFindPreviousClick(sender, e);

    private void FindNextButton_OnClick(object? sender, RoutedEventArgs e) =>
        EditorHost?.OnNoteFindPanelFindNextClick(sender, e);

    private void FindToggleReplaceButton_OnClick(object? sender, RoutedEventArgs e) =>
        EditorHost?.OnNoteFindPanelToggleReplaceClick(sender, e);

    private void FindCloseButton_OnClick(object? sender, RoutedEventArgs e) =>
        EditorHost?.OnNoteFindPanelCloseClick(sender, e);

    private void ReplaceCurrentButton_OnClick(object? sender, RoutedEventArgs e) =>
        EditorHost?.OnNoteFindPanelReplaceCurrentClick(sender, e);

    private void ReplaceAllButton_OnClick(object? sender, RoutedEventArgs e) =>
        EditorHost?.OnNoteFindPanelReplaceAllClick(sender, e);

    private void FindTextBox_OnKeyDown(object? sender, KeyEventArgs e) =>
        EditorHost?.OnNoteFindPanelFindTextKeyDown(sender, e);

    private void ReplaceTextBox_OnKeyDown(object? sender, KeyEventArgs e) =>
        EditorHost?.OnNoteFindPanelReplaceTextKeyDown(sender, e);
}
