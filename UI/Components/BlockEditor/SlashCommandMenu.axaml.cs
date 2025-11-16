using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using MnemoApp.Modules.Notes.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MnemoApp.UI.Components.BlockEditor;

public class CommandItem
{
    public string Icon { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public BlockType BlockType { get; set; }
}

public partial class SlashCommandMenu : UserControl
{
    private const int MENU_OFFSET = 4;
    private readonly List<CommandItem> _allCommandItems;
    private List<CommandItem> _filteredCommandItems;
    private Control? _parentControl;
    private string _filterText = string.Empty;

    public event Action<BlockType>? CommandSelected;

    public bool IsVisibleMenu
    {
        get => IsVisible;
        set => IsVisible = value;
    }

    public SlashCommandMenu()
    {
        _allCommandItems = InitializeCommandItems();
        _filteredCommandItems = new List<CommandItem>(_allCommandItems);
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        EnsureItemsSourceSet();
        Loaded -= OnLoaded; // Unsubscribe after setting
    }

    private void EnsureItemsSourceSet()
    {
        var itemsControl = this.FindControl<ItemsControl>("CommandItems");
        if (itemsControl != null)
        {
            if (itemsControl.ItemsSource != _filteredCommandItems)
            {
                itemsControl.ItemsSource = _filteredCommandItems;
                System.Diagnostics.Debug.WriteLine($"[SlashCommandMenu] Set ItemsSource with {_filteredCommandItems.Count} items");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[SlashCommandMenu] CommandItems not found!");
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private List<CommandItem> InitializeCommandItems()
    {
        return new List<CommandItem>
        {
            new() { Icon = "T", Name = "Text", Description = "Plain text block", BlockType = BlockType.Text },
            new() { Icon = "H1", Name = "Heading 1", Description = "Large section heading", BlockType = BlockType.Heading1 },
            new() { Icon = "H2", Name = "Heading 2", Description = "Medium section heading", BlockType = BlockType.Heading2 },
            new() { Icon = "H3", Name = "Heading 3", Description = "Small section heading", BlockType = BlockType.Heading3 },
            new() { Icon = "•", Name = "Bullet List", Description = "Simple bulleted list", BlockType = BlockType.BulletList },
            new() { Icon = "1.", Name = "Numbered List", Description = "Ordered numbered list", BlockType = BlockType.NumberedList },
            new() { Icon = "☑", Name = "Checklist", Description = "Interactive to-do item", BlockType = BlockType.Checklist },
            new() { Icon = "Q", Name = "Quote", Description = "Quoted text block", BlockType = BlockType.Quote },
            new() { Icon = "</>", Name = "Code", Description = "Code block with syntax highlighting", BlockType = BlockType.Code },
            new() { Icon = "—", Name = "Divider", Description = "Horizontal divider line", BlockType = BlockType.Divider }
        };
    }

    public void Show(TextBox textBox, Control parentControl, string filterText = "")
    {
        if (textBox == null || !textBox.IsVisible) return;

        _parentControl = parentControl;
        _filterText = filterText;
        FilterItems(filterText);
        EnsureItemsSourceSet(); // Ensure items are set before showing
        IsVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => PositionMenu(textBox),
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    public void UpdateFilter(string filterText)
    {
        if (_filterText == filterText) return;
        
        _filterText = filterText;
        FilterItems(filterText);
        EnsureItemsSourceSet();
    }

    private void FilterItems(string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText) || filterText == "/")
        {
            _filteredCommandItems = new List<CommandItem>(_allCommandItems);
        }
        else
        {
            var searchTerm = filterText.TrimStart('/').ToLowerInvariant();
            _filteredCommandItems = _allCommandItems
                .Where(item => item.Name.ToLowerInvariant().Contains(searchTerm) ||
                              item.Description.ToLowerInvariant().Contains(searchTerm))
                .ToList();
        }
    }

    public void Hide()
    {
        IsVisible = false;
    }

    public void HandleEnter()
    {
        if (!IsVisible || _filteredCommandItems.Count == 0) return;

        CommandSelected?.Invoke(_filteredCommandItems[0].BlockType);
        Hide();
    }

    public void HandleItemClick(CommandItem command)
    {
        if (command == null) return;

        CommandSelected?.Invoke(command.BlockType);
        Hide();
    }

    private void PositionMenu(TextBox textBox)
    {
        if (!textBox.IsVisible || _parentControl == null) return;

        try
        {
            // Find the block container (Border) that contains the textbox
            var blockContainer = _parentControl.GetVisualChildren().OfType<Border>().FirstOrDefault();
            if (blockContainer == null)
            {
                // Fallback to grid if border not found
                var grid = _parentControl.GetVisualChildren().OfType<Grid>().FirstOrDefault();
                if (grid == null) return;

                var textBoxBounds = textBox.Bounds;
                var textBoxPosition = textBox.TranslatePoint(new Point(0, textBoxBounds.Height), grid);

                if (textBoxPosition.HasValue)
                {
                    Margin = new Thickness(
                        textBoxPosition.Value.X,
                        textBoxPosition.Value.Y + MENU_OFFSET,
                        0,
                        0
                    );
                }
                return;
            }

            // Position menu underneath the block container
            var blockBounds = blockContainer.Bounds;
            var blockPosition = blockContainer.TranslatePoint(new Point(0, blockBounds.Height), _parentControl);

            if (blockPosition.HasValue)
            {
                Margin = new Thickness(
                    blockPosition.Value.X,
                    blockPosition.Value.Y + MENU_OFFSET,
                    0,
                    0
                );
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SlashCommandMenu] Error positioning menu: {ex.Message}");
            Margin = new Thickness(32, 0, 0, 0);
        }
    }

    private void CommandItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not CommandItem command)
        {
            return;
        }

        HandleItemClick(command);
        e.Handled = true;
    }
}

