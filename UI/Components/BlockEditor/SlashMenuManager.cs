using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using MnemoApp.Modules.Notes.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MnemoApp.UI.Components.BlockEditor;

public class SlashMenuManager
{
    private const int MENU_OFFSET = 4;
    
    private readonly Border _menuBorder;
    private readonly ItemsControl _itemsControl;
    private readonly Control _parentControl;
    private readonly List<CommandItem> _commandItems;
    private bool _isVisible;

    public event Action<BlockType>? CommandSelected;

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            _isVisible = value;
            _menuBorder.IsVisible = value;
        }
    }

    public SlashMenuManager(Border menuBorder, ItemsControl itemsControl, Control parentControl)
    {
        _menuBorder = menuBorder ?? throw new ArgumentNullException(nameof(menuBorder));
        _itemsControl = itemsControl ?? throw new ArgumentNullException(nameof(itemsControl));
        _parentControl = parentControl ?? throw new ArgumentNullException(nameof(parentControl));
        
        _commandItems = InitializeCommandItems();
        _itemsControl.ItemsSource = _commandItems;
        IsVisible = false;
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

    public void Show(TextBox textBox)
    {
        if (IsVisible || textBox == null || !textBox.IsVisible) return;
        
        IsVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => PositionMenu(textBox), 
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    public void Hide()
    {
        IsVisible = false;
    }

    public void HandleEnter()
    {
        if (!IsVisible || _commandItems.Count == 0) return;
        
        CommandSelected?.Invoke(_commandItems[0].BlockType);
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
        if (!textBox.IsVisible) return;
        
        try
        {
            var grid = _parentControl.GetVisualChildren().OfType<Grid>().FirstOrDefault();
            if (grid == null) return;
            
            var textBoxBounds = textBox.Bounds;
            var textBoxPosition = textBox.TranslatePoint(new Point(0, textBoxBounds.Height), grid);
            
            if (textBoxPosition.HasValue)
            {
                _menuBorder.Margin = new Thickness(
                    textBoxPosition.Value.X, 
                    textBoxPosition.Value.Y + MENU_OFFSET, 
                    0, 
                    0
                );
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SlashMenuManager] Error positioning menu: {ex.Message}");
            _menuBorder.Margin = new Thickness(32, 0, 0, 0);
        }
    }
}

