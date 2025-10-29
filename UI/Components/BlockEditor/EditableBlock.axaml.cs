using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using MnemoApp.Modules.Notes.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MnemoApp.UI.Components.BlockEditor;

public partial class EditableBlock : UserControl
{
    private BlockViewModel? _viewModel;
    private bool _isSlashMenuVisible;
    private List<CommandItem> _commandItems = new();
    private Key? _lastKeyPressed;
    private string _previousText = string.Empty;
    private bool _isUpdatingFromViewModel = false;

    public EditableBlock()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        InitializeCommandItems();
        
        // Set up drag and drop events
        if (BlockContainer != null)
        {
            DragDrop.SetAllowDrop(BlockContainer, true);
            BlockContainer.AddHandler(DragDrop.DragOverEvent, Block_DragOver);
            BlockContainer.AddHandler(DragDrop.DropEvent, Block_Drop);
        }
        
        // Add KeyDown handler at UserControl level to catch events before TextBox consumes them
        this.AddHandler(KeyDownEvent, UserControl_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble);
    }
    
    private void UserControl_KeyDown(object? sender, KeyEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[UserControl_KeyDown] Key={e.Key}, Handled={e.Handled}");
        
        // Don't process if already handled or if ViewModel is null
        if (e.Handled || _viewModel == null) return;
        
        // Find the focused TextBox by searching our descendants
        var textBox = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(tb => tb.IsFocused);
        
        if (textBox != null)
        {
            var text = textBox.Text ?? string.Empty;
            var caretIndex = textBox.CaretIndex;
            var isTextBoxEmpty = string.IsNullOrWhiteSpace(text);
            var isContentEmpty = string.IsNullOrWhiteSpace(_viewModel.Content ?? string.Empty);
            
            System.Diagnostics.Debug.WriteLine($"[UserControl_KeyDown] TextBox found - Text='{text}', CaretIndex={caretIndex}, isEmpty={isTextBoxEmpty}");
            
            // Handle Backspace on empty block
            if (e.Key == Key.Back && (isTextBoxEmpty || isContentEmpty) && (caretIndex <= 1 || text.Length == 0))
            {
                System.Diagnostics.Debug.WriteLine($"[UserControl_KeyDown] Handling backspace on empty block - Type={_viewModel.Type}");
                e.Handled = true;
                
                // For regular text blocks (including headings), delete and focus above
                if (_viewModel.Type == BlockType.Text ||
                    _viewModel.Type == BlockType.Heading1 ||
                    _viewModel.Type == BlockType.Heading2 ||
                    _viewModel.Type == BlockType.Heading3)
                {
                    System.Diagnostics.Debug.WriteLine("[UserControl_KeyDown] Deleting text/heading block");
                    _viewModel.RequestDeleteAndFocusAbove();
                }
                // For other types (lists, code, quote, etc.), convert to text block first
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[UserControl_KeyDown] Converting {_viewModel.Type} to Text");
                    _isUpdatingFromViewModel = true;
                    _viewModel.Type = BlockType.Text;
                    _viewModel.Content = string.Empty;
                    _previousText = string.Empty;
                    _isUpdatingFromViewModel = false;
                }
            }
        }
    }
    
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from previous view model
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as BlockViewModel;

        // Subscribe to new view model
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsFocused" && _viewModel != null && _viewModel.IsFocused)
        {
            // Focus the appropriate TextBox when IsFocused becomes true
            FocusTextBox();
        }
    }

    private void FocusTextBox()
    {
        if (_viewModel == null) return;

        // Use dispatcher to ensure UI is ready
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Find the TextBox based on block type - search deeper in visual tree
            var textBoxes = this.GetVisualDescendants().OfType<TextBox>().ToList();
            foreach (var textBox in textBoxes)
            {
                if (textBox.IsVisible && textBox.IsEffectivelyVisible)
                {
                    textBox.Focus();
                    textBox.CaretIndex = textBox.Text?.Length ?? 0;
                    break;
                }
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void InitializeCommandItems()
    {
        _commandItems = new List<CommandItem>
        {
            new CommandItem { Icon = "📝", Name = "Text", Description = "Plain text block", BlockType = BlockType.Text },
            new CommandItem { Icon = "H1", Name = "Heading 1", Description = "Large section heading", BlockType = BlockType.Heading1 },
            new CommandItem { Icon = "H2", Name = "Heading 2", Description = "Medium section heading", BlockType = BlockType.Heading2 },
            new CommandItem { Icon = "H3", Name = "Heading 3", Description = "Small section heading", BlockType = BlockType.Heading3 },
            new CommandItem { Icon = "•", Name = "Bullet List", Description = "Simple bulleted list", BlockType = BlockType.BulletList },
            new CommandItem { Icon = "1.", Name = "Numbered List", Description = "Ordered numbered list", BlockType = BlockType.NumberedList },
            new CommandItem { Icon = "☑", Name = "Checklist", Description = "Interactive to-do item", BlockType = BlockType.Checklist },
            new CommandItem { Icon = "💬", Name = "Quote", Description = "Quoted text block", BlockType = BlockType.Quote },
            new CommandItem { Icon = "</>", Name = "Code", Description = "Code block with syntax highlighting", BlockType = BlockType.Code },
            new CommandItem { Icon = "—", Name = "Divider", Description = "Horizontal divider line", BlockType = BlockType.Divider }
        };
        
        CommandItems.ItemsSource = _commandItems;
    }

    private void TextBox_GotFocus(object? sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[GotFocus] Event fired");
        
        if (_viewModel != null)
        {
            _viewModel.IsFocused = true;
            // Initialize previous text when block is focused
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                _previousText = textBox.Text ?? string.Empty;
                System.Diagnostics.Debug.WriteLine($"[GotFocus] TextBox focused, Text='{_previousText}', HasFocus={textBox.IsFocused}");
                
                // Ensure KeyDown handler is attached (redundant but safe - harmless if already attached)
                textBox.KeyDown += TextBox_KeyDown;
                System.Diagnostics.Debug.WriteLine("[GotFocus] KeyDown handler attached programmatically");
            }
        }
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.IsFocused = false;
        }
    }

    private void TextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[TextChanged] Fired");
        
        var textBox = sender as TextBox;
        if (textBox == null || _viewModel == null)
        {
            System.Diagnostics.Debug.WriteLine("[TextChanged] TextBox or ViewModel is null");
            return;
        }
        
        // Ignore changes triggered by ViewModel updates (binding loops)
        if (_isUpdatingFromViewModel)
        {
            System.Diagnostics.Debug.WriteLine("[TextChanged] Ignored - updating from ViewModel");
            return;
        }
        
        var text = textBox.Text ?? string.Empty;
        
        // Check if block became empty due to backspace (had content before, now empty)
        var hadContentBefore = !string.IsNullOrWhiteSpace(_previousText);
        var isEmptyNow = string.IsNullOrWhiteSpace(text);
        var isBackspacePress = _lastKeyPressed == Key.Back;
        
        if (hadContentBefore && isEmptyNow && isBackspacePress)
        {
            _lastKeyPressed = null; // Reset before handling
            
            // For regular text blocks (including headings), delete and focus above
            if (_viewModel.Type == BlockType.Text ||
                _viewModel.Type == BlockType.Heading1 ||
                _viewModel.Type == BlockType.Heading2 ||
                _viewModel.Type == BlockType.Heading3)
            {
                // Use dispatcher to avoid modifying during TextChanged
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _viewModel.RequestDeleteAndFocusAbove();
                }, Avalonia.Threading.DispatcherPriority.Input);
                return;
            }
            // For other types (lists, code, quote, etc.), convert to text block first
            else
            {
                // Use dispatcher to avoid modifying during TextChanged
                _isUpdatingFromViewModel = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _viewModel.Type = BlockType.Text;
                    _viewModel.Content = string.Empty;
                    _previousText = string.Empty;
                    _isUpdatingFromViewModel = false;
                }, Avalonia.Threading.DispatcherPriority.Input);
                return;
            }
        }
        
        // Check if user just typed "/" at the beginning (text is exactly "/")
        if (text == "/" && !_isSlashMenuVisible)
        {
            ShowSlashMenu(textBox);
        }
        // Check if "/" was deleted or moved - hide menu if text doesn't start with "/"
        else if (_isSlashMenuVisible && !text.StartsWith("/"))
        {
            HideSlashMenu();
        }
        
        // Update ViewModel content only if it changed (prevents binding loops)
        if (_viewModel.Content != text)
        {
            _viewModel.Content = text;
        }
        
        _viewModel?.NotifyContentChanged();
        
        // Store current text for next comparison
        _previousText = text;
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[KeyDown] Key={e.Key}, Handled={e.Handled}");
        
        // Track the last key pressed for TextChanged handler
        _lastKeyPressed = e.Key;
        
        if (_viewModel == null)
        {
            System.Diagnostics.Debug.WriteLine("[KeyDown] ViewModel is null");
            return;
        }

        var textBox = sender as TextBox;
        if (textBox == null)
        {
            System.Diagnostics.Debug.WriteLine("[KeyDown] TextBox is null");
            return;
        }

        var text = textBox.Text ?? string.Empty;
        var caretIndex = textBox.CaretIndex;
        var textLength = text.Length;
        
        System.Diagnostics.Debug.WriteLine($"[KeyDown] Text='{text}', CaretIndex={caretIndex}, Type={_viewModel.Type}");

        // Handle Enter key - create new block below or continue list
        if (e.Key == Key.Enter && _viewModel.Type != BlockType.Code)
        {
            e.Handled = true;
            HandleEnterKey(textBox);
        }
        // Handle Backspace on empty block
        else if (e.Key == Key.Back)
        {
            System.Diagnostics.Debug.WriteLine($"[KeyDown] Backspace detected - Text='{text}', CaretIndex={caretIndex}");
            
            var isTextBoxEmpty = string.IsNullOrWhiteSpace(text);
            var isContentEmpty = string.IsNullOrWhiteSpace(_viewModel.Content ?? string.Empty);
            
            System.Diagnostics.Debug.WriteLine($"[KeyDown] isEmpty: TextBox={isTextBoxEmpty}, Content={isContentEmpty}");
            
            // If block is empty, handle deletion/conversion (regardless of caret position for empty blocks)
            if (isTextBoxEmpty || isContentEmpty)
            {
                // Only process if we're at the start OR the text is completely empty (Length == 0)
                if (caretIndex <= 1 || text.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[KeyDown] Handling backspace on empty block - Type={_viewModel.Type}");
                    e.Handled = true;
                    
                    // For regular text blocks (including headings), delete and focus above
                    if (_viewModel.Type == BlockType.Text ||
                        _viewModel.Type == BlockType.Heading1 ||
                        _viewModel.Type == BlockType.Heading2 ||
                        _viewModel.Type == BlockType.Heading3)
                    {
                        System.Diagnostics.Debug.WriteLine("[KeyDown] Deleting text/heading block");
                        _viewModel.RequestDeleteAndFocusAbove();
                    }
                    // For other types (lists, code, quote, etc.), convert to text block first
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[KeyDown] Converting {_viewModel.Type} to Text");
                        _isUpdatingFromViewModel = true;
                        _viewModel.Type = BlockType.Text;
                        _viewModel.Content = string.Empty;
                        _previousText = string.Empty;
                        _isUpdatingFromViewModel = false;
                    }
                    return;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[KeyDown] Empty block but caretIndex={caretIndex} and text.Length={text.Length}");
                }
            }
        }
        // Handle Up arrow - focus previous block when at start of text or on first line
        else if (e.Key == Key.Up && (caretIndex == 0 || IsOnFirstLine(textBox)))
        {
            e.Handled = true;
            _viewModel.RequestFocusPrevious();
        }
        // Handle Down arrow - focus next block when at end of text or on last line
        else if (e.Key == Key.Down && (caretIndex == textLength || IsOnLastLine(textBox)))
        {
            e.Handled = true;
            _viewModel.RequestFocusNext();
        }
        // Escape key - hide menu
        else if (e.Key == Key.Escape && _isSlashMenuVisible)
        {
            HideSlashMenu();
            e.Handled = true;
        }
        // Detect markdown shortcuts
        else if (e.Key == Key.Space)
        {
            DetectMarkdownShortcuts(textBox);
        }
    }

    private bool IsOnFirstLine(TextBox textBox)
    {
        // For single-line textboxes or when at caret 0, always on first line
        if (!textBox.AcceptsReturn || textBox.CaretIndex == 0)
            return true;

        var text = textBox.Text ?? string.Empty;
        var caretIndex = textBox.CaretIndex;
        
        // Check if there are any newlines before the caret
        return !text.Substring(0, caretIndex).Contains('\n');
    }

    private bool IsOnLastLine(TextBox textBox)
    {
        // For single-line textboxes or when at end, always on last line
        if (!textBox.AcceptsReturn || textBox.CaretIndex == (textBox.Text?.Length ?? 0))
            return true;

        var text = textBox.Text ?? string.Empty;
        var caretIndex = textBox.CaretIndex;
        
        // Check if there are any newlines after the caret
        return !text.Substring(caretIndex).Contains('\n');
    }

    private void HandleEnterKey(TextBox textBox)
    {
        if (_viewModel == null) return;

        var hasContent = !string.IsNullOrWhiteSpace(textBox.Text);

        // For list types, handle continuation logic
        switch (_viewModel.Type)
        {
            case BlockType.BulletList:
                if (hasContent)
                {
                    _viewModel.RequestNewBlockOfType(BlockType.BulletList);
                }
                else
                {
                    // Empty list item - convert to text block
                    _viewModel.Type = BlockType.Text;
                    _viewModel.Content = string.Empty;
                }
                break;

            case BlockType.NumberedList:
                if (hasContent)
                {
                    _viewModel.RequestNewBlockOfType(BlockType.NumberedList);
                }
                else
                {
                    // Empty list item - convert to text block
                    _viewModel.Type = BlockType.Text;
                    _viewModel.Content = string.Empty;
                }
                break;

            case BlockType.Checklist:
                if (hasContent)
                {
                    _viewModel.RequestNewBlockOfType(BlockType.Checklist);
                }
                else
                {
                    // Empty checklist item - convert to text block
                    _viewModel.Type = BlockType.Text;
                    _viewModel.Content = string.Empty;
                }
                break;

            default:
                // Default behavior - create new text block
                _viewModel.RequestNewBlock();
                break;
        }
    }

    private void DetectMarkdownShortcuts(TextBox textBox)
    {
        if (_viewModel == null) return;

        var text = textBox.Text ?? string.Empty;
        var trimmed = text.Trim();
        
        var conversion = trimmed switch
        {
            "#" => (BlockType.Heading1, (Dictionary<string, object>?)null),
            "##" => (BlockType.Heading2, (Dictionary<string, object>?)null),
            "###" => (BlockType.Heading3, (Dictionary<string, object>?)null),
            "-" or "*" => (BlockType.BulletList, (Dictionary<string, object>?)null),
            "[]" or "[ ]" => (BlockType.Checklist, (Dictionary<string, object>?)null),
            ">" => (BlockType.Quote, (Dictionary<string, object>?)null),
            "```" => (BlockType.Code, new Dictionary<string, object> { ["language"] = "csharp" }),
            _ when trimmed.StartsWith("1.") => (BlockType.NumberedList, (Dictionary<string, object>?)null),
            _ => ((BlockType?)null, (Dictionary<string, object>?)null)
        };

        if (conversion.Item1.HasValue)
        {
            _viewModel.Type = conversion.Item1.Value;
            _viewModel.Content = string.Empty;
            
            // Apply any meta changes
            if (conversion.Item2 != null)
            {
                foreach (var kvp in conversion.Item2)
                {
                    _viewModel.Meta[kvp.Key] = kvp.Value;
                }
            }

            // Focus the TextBox after conversion
            FocusTextBox();
        }
    }

    private void DragHandle_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 1;
        }
    }

    private void DragHandle_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 0;
        }
    }

    private void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel == null) return;

        var data = new DataObject();
        data.Set("BlockViewModel", _viewModel);

        DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void Block_DragOver(object? sender, DragEventArgs e)
    {
        // Allow dropping blocks
        if (e.Data.Contains("BlockViewModel"))
        {
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void Block_Drop(object? sender, DragEventArgs e)
    {
        if (_viewModel == null) return;

        if (e.Data.Get("BlockViewModel") is BlockViewModel draggedBlock)
        {
            // Find the BlockEditor parent to handle reordering
            var parent = FindParentBlockEditor();
            if (parent != null)
            {
                var draggedIndex = parent.Blocks.IndexOf(draggedBlock);
                var targetIndex = parent.Blocks.IndexOf(_viewModel);

                if (draggedIndex != -1 && targetIndex != -1 && draggedIndex != targetIndex)
                {
                    parent.Blocks.Move(draggedIndex, targetIndex);
                    
                    // Update order and trigger save
                    for (int i = 0; i < parent.Blocks.Count; i++)
                    {
                        parent.Blocks[i].Order = i;
                    }
                    
                    // Notify parent that blocks changed
                    parent.NotifyBlocksChanged();
                }
            }
        }
    }

    private BlockEditor? FindParentBlockEditor()
    {
        // Try visual tree first
        var current = this.GetVisualParent();
        while (current != null)
        {
            if (current is BlockEditor blockEditor)
            {
                return blockEditor;
            }
            current = current.GetVisualParent();
        }
        
        // Try logical tree as fallback
        var logicalCurrent = this.GetLogicalParent();
        while (logicalCurrent != null)
        {
            if (logicalCurrent is BlockEditor blockEditor)
            {
                return blockEditor;
            }
            logicalCurrent = logicalCurrent.GetLogicalParent();
        }
        
        return null;
    }

    private void ShowSlashMenu(TextBox textBox)
    {
        if (_isSlashMenuVisible) return;
        
        _isSlashMenuVisible = true;
        SlashCommandMenu.IsVisible = true;
        
        // Keep the "/" character in the text
    }

    private void HideSlashMenu()
    {
        _isSlashMenuVisible = false;
        SlashCommandMenu.IsVisible = false;
    }

    private void CommandItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is CommandItem command && _viewModel != null)
        {
            _viewModel.Type = command.BlockType;
            _viewModel.Content = string.Empty;
            HideSlashMenu();
            
            // Focus the TextBox after command selection
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                FocusTextBox();
            });
            
            e.Handled = true;
        }
    }
}

public class CommandItem
{
    public string Icon { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public BlockType BlockType { get; set; }
}

