using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MnemoApp.Modules.Notes.Models;
using System;
using System.Linq;

namespace MnemoApp.UI.Components.BlockEditor;

/// <summary>
/// Improved EditableBlock with better separation of concerns,
/// improved performance through caching, and cleaner architecture.
/// </summary>
public partial class EditableBlock : UserControl
{
    // Manager instances
    private SlashMenuManager? _slashMenuManager;
    private KeyboardHandler? _keyboardHandler;
    private FocusManager? _focusManager;
    private MarkdownShortcutDetector? _markdownDetector;
    private EditorStateManager? _stateManager;
    
    private BlockViewModel? _viewModel;
    private BlockEditor? _cachedParentEditor;

    public EditableBlock()
    {
        InitializeComponent();
        InitializeManagers();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnControlLoaded;
        Unloaded += OnControlUnloaded;
        
        SetupDragDrop();
        SetupKeyboardHandling();
    }

    private void InitializeManagers()
    {
        _stateManager = new EditorStateManager();
        _keyboardHandler = new KeyboardHandler();
        _markdownDetector = new MarkdownShortcutDetector();
        _focusManager = new FocusManager(this);

        // Wire up keyboard handler events - use method groups where possible to avoid lambda allocations
        _keyboardHandler.BackspaceOnEmpty += HandleBackspaceOnEmptyBlock;
        _keyboardHandler.RequestFocusPrevious += HandleRequestFocusPrevious;
        _keyboardHandler.RequestFocusNext += HandleRequestFocusNext;
        _keyboardHandler.EnterPressed += HandleEnterPressed;
        _keyboardHandler.RequestNewBlockOfType += HandleRequestNewBlockOfType;
        _keyboardHandler.ConvertToBlockType += ConvertToBlockType;
        _keyboardHandler.EscapePressed += OnEscapePressed;

        // Wire up markdown detector
        _markdownDetector.ShortcutDetected += OnMarkdownShortcutDetected;
    }

    private void HandleRequestFocusPrevious() => _viewModel?.RequestFocusPrevious();
    private void HandleRequestFocusNext() => _viewModel?.RequestFocusNext();
    private void HandleEnterPressed() => _viewModel?.RequestNewBlock();
    private void HandleRequestNewBlockOfType(BlockType type) => _viewModel?.RequestNewBlockOfType(type);
    private void OnMarkdownShortcutDetected(BlockType type, System.Collections.Generic.Dictionary<string, object>? meta) => SetBlockType(type, meta);

    private void OnControlUnloaded(object? sender, RoutedEventArgs e)
    {
        CleanupEventHandlers();
    }

    private void CleanupEventHandlers()
    {
        UnsubscribeFromViewModel();
        UnsubscribeFromManagers();
        
        DataContextChanged -= OnDataContextChanged;
        Loaded -= OnControlLoaded;
        Unloaded -= OnControlUnloaded;
        
        _cachedParentEditor = null;
        _focusManager?.ClearCache();
    }

    private void UnsubscribeFromManagers()
    {
        if (_keyboardHandler != null)
        {
            _keyboardHandler.BackspaceOnEmpty -= HandleBackspaceOnEmptyBlock;
            _keyboardHandler.RequestFocusPrevious -= HandleRequestFocusPrevious;
            _keyboardHandler.RequestFocusNext -= HandleRequestFocusNext;
            _keyboardHandler.EnterPressed -= HandleEnterPressed;
        _keyboardHandler.RequestNewBlockOfType -= HandleRequestNewBlockOfType;
        _keyboardHandler.ConvertToBlockType -= ConvertToBlockType;
        _keyboardHandler.EscapePressed -= OnEscapePressed;
        }
        
        if (_markdownDetector != null)
        {
            _markdownDetector.ShortcutDetected -= OnMarkdownShortcutDetected;
        }
        
        if (_slashMenuManager != null)
        {
            _slashMenuManager.CommandSelected -= OnCommandSelected;
        }
    }

    private void OnControlLoaded(object? sender, RoutedEventArgs e)
    {
        // Initialize managers that depend on XAML elements after control is loaded
        if (SlashCommandMenu != null && CommandItems != null && _slashMenuManager == null)
        {
            _slashMenuManager = new SlashMenuManager(SlashCommandMenu, CommandItems, this);
            _slashMenuManager.CommandSelected += OnCommandSelected;
        }
    }

    private void SetupDragDrop()
    {
        if (BlockContainer == null) return;

        DragDrop.SetAllowDrop(BlockContainer, true);
        BlockContainer.AddHandler(DragDrop.DragOverEvent, Block_DragOver);
        BlockContainer.AddHandler(DragDrop.DropEvent, Block_Drop);
    }

    private void SetupKeyboardHandling()
    {
        // Keyboard handling is done via TextBox_KeyDown which delegates to KeyboardHandler
        // No need for UserControl-level handler
    }

    #region DataContext and ViewModel Management

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeFromViewModel();
        
        _viewModel = DataContext as BlockViewModel;
        _cachedParentEditor = null; // Invalidate cache when context changes
        _focusManager?.ClearCache(); // Clear textbox cache too
        
        SubscribeToViewModel();
        
        if (_viewModel != null && _stateManager != null)
        {
            _stateManager.SetUpdatingFromViewModel();
            _stateManager.PreviousText = _viewModel.Content ?? string.Empty;
            
            Dispatcher.UIThread.Post(() => _stateManager.SetNormal(), DispatcherPriority.Loaded);
        }
    }

    private void SubscribeToViewModel()
    {
        if (_viewModel == null) return;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void UnsubscribeFromViewModel()
    {
        if (_viewModel == null) return;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_stateManager == null || _viewModel == null) return;

        switch (e.PropertyName)
        {
            case nameof(BlockViewModel.IsFocused) when _viewModel.IsFocused:
                _focusManager?.ClearCache(); // Clear cache when focus changes
                _focusManager?.FocusTextBox();
                break;
                
            case nameof(BlockViewModel.Content):
                if (_stateManager != null)
                {
                    var currentText = _viewModel.Content ?? string.Empty;
                    
                    // Check if TextBox text already matches the new Content value
                    // If so, this PropertyChanged was triggered by TextChanged handler updating Content,
                    // so we should NOT set the updating flag (to avoid blocking the next keystroke)
                    var textBox = _focusManager?.GetCurrentTextBox();
                    var textBoxText = textBox?.Text ?? string.Empty;
                    
                    if (textBoxText == currentText)
                    {
                        // TextBox text matches Content - this is from TextChanged handler
                        // Don't set the updating flag, just sync PreviousText
                        // TextChanged handler will update PreviousText, but sync here too for safety
                        _stateManager.PreviousText = currentText;
                        return;
                    }
                    
                    // This is a programmatic update from outside (e.g., loading), set the flag
                    _stateManager.SetUpdatingFromViewModel();
                    _stateManager.PreviousText = currentText;
                    _focusManager?.ClearCache();
                    
                    Dispatcher.UIThread.Post(() => _stateManager.SetNormal(), DispatcherPriority.Render);
                }
                break;
        }
    }

    #endregion

    #region TextBox Event Handlers

    private void TextBox_GotFocus(object? sender, RoutedEventArgs e)
    {
        BlockEditorLogger.Log("TextBox focused");
        
        if (_viewModel == null || _stateManager == null) return;

        _viewModel.IsFocused = true;
        
        if (sender is TextBox textBox)
        {
            _stateManager.PreviousText = textBox.Text ?? string.Empty;
        }
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null && _stateManager != null)
        {
            _viewModel.IsFocused = false;
            // Notify content changed when focus is lost, so unsaved changes get saved
            // This ensures saves happen even when just clicking away from a block
            _viewModel.NotifyContentChanged();
        }
    }

    private void TextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        BlockEditorLogger.Log($"TextBox_TextChanged called");
        
        if (sender is not TextBox textBox || _viewModel == null || _stateManager == null)
        {
            BlockEditorLogger.Log("TextBox_TextChanged: early return - null check");
            return;
        }
        
        if (_stateManager.IsUpdatingFromViewModel)
        {
            BlockEditorLogger.Log("Ignored - updating from ViewModel");
            return;
        }
        
        var text = textBox.Text ?? string.Empty;
        var previousText = _stateManager.PreviousText;
        
        // Always sync slash menu state FIRST - it's independent of whether text changed
        // This ensures menu state is correct even if PreviousText tracking is off
        HandleSlashMenuToggle(text, textBox);
        
        // Skip other handlers if text hasn't actually changed (e.g., programmatic updates)
        if (text == previousText)
        {
            BlockEditorLogger.Log($"TextBox_TextChanged: text unchanged '{text}' == '{previousText}'");
            return;
        }
        
        BlockEditorLogger.LogTextChanged(text, previousText, _slashMenuManager?.IsVisible ?? false);
        
        HandleEmptyBlockBackspace(text);
        
        // Update ViewModel content - always update to ensure binding is in sync
        // The Content setter will only fire PropertyChanged if value actually changed
        _viewModel.Content = text;
        
        // Notify content changed AFTER updating the Content property
        // This ensures the save mechanism is triggered on every user edit
        // Note: We call this AFTER setting Content so the event fires even if
        // PropertyChanged handler sets IsUpdatingFromViewModel flag
        _viewModel.NotifyContentChanged();
        _stateManager.PreviousText = text;
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        BlockEditorLogger.LogKeyEvent(e.Key.ToString(), e.Handled);
        
        if (_viewModel == null || sender is not TextBox textBox || _keyboardHandler == null)
        {
            return;
        }

        // Handle markdown shortcuts on space
        if (e.Key == Key.Space)
        {
            _markdownDetector?.TryDetectShortcut(textBox);
        }

        // Optimistic slash menu handling: show immediately on slash key when textbox is empty
        // TextChanged will validate and correct state if needed (handles all edge cases)
        var isSlashKey = e.Key == Key.Divide || e.Key == Key.OemQuestion || e.Key == Key.Oem2;
        if (isSlashKey && string.IsNullOrWhiteSpace(textBox.Text) && _slashMenuManager != null && _stateManager != null && !_slashMenuManager.IsVisible)
        {
            // Text will become "/" after this keypress - show menu optimistically for immediate feedback
            // TextChanged will validate this and correct if needed
            Dispatcher.UIThread.Post(() =>
            {
                var currentText = textBox.Text ?? string.Empty;
                if (currentText == "/" && _slashMenuManager != null && !_slashMenuManager.IsVisible)
                {
                    BlockEditorLogger.Log("Optimistically showing slash menu from KeyDown");
                    _slashMenuManager.Show(textBox);
                    _stateManager?.SetShowingSlashMenu();
                }
            }, DispatcherPriority.Input);
        }

        // Fallback: Hide menu if visible and any non-slash key is pressed (covers cases where TextChanged doesn't fire)
        // This ensures menu state is always correct regardless of event timing
        if (_slashMenuManager?.IsVisible == true && !isSlashKey && e.Key != Key.Escape && e.Key != Key.Enter && e.Key != Key.Up && e.Key != Key.Down)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var currentText = textBox.Text ?? string.Empty;
                if (currentText != "/" && _slashMenuManager != null && _stateManager != null)
                {
                    BlockEditorLogger.Log($"Hiding slash menu (KeyDown fallback) - text is '{currentText}'");
                    _slashMenuManager.Hide();
                    _stateManager.SetNormal();
                }
            }, DispatcherPriority.Input);
        }

        // Delegate to keyboard handler
        _keyboardHandler.HandleKeyDown(e, textBox, _viewModel);
    }

    #endregion

    #region Keyboard and Input Handling

    private void HandleBackspaceOnEmptyBlock()
    {
        if (_keyboardHandler == null || _viewModel == null) return;
        _keyboardHandler.HandleBackspaceOnEmptyBlock(_viewModel);
    }

    private void HandleSlashMenuToggle(string text, TextBox textBox)
    {
        if (_slashMenuManager == null || _stateManager == null)
        {
            BlockEditorLogger.Log("HandleSlashMenuToggle: managers null");
            return;
        }

        // Source of truth: menu should be visible if and only if text is exactly "/"
        var isSlashOnly = text == "/";
        var menuIsVisible = _slashMenuManager.IsVisible;

        BlockEditorLogger.Log($"HandleSlashMenuToggle: text='{text}', isSlashOnly={isSlashOnly}, menuIsVisible={menuIsVisible}");

        // Always ensure state matches reality - correct any mismatches
        if (isSlashOnly && !menuIsVisible)
        {
            BlockEditorLogger.Log("Showing slash menu (TextChanged)");
            _slashMenuManager.Show(textBox);
            _stateManager.SetShowingSlashMenu();
        }
        else if (!isSlashOnly && menuIsVisible)
        {
            BlockEditorLogger.Log($"Hiding slash menu (TextChanged) - text changed to '{text}'");
            _slashMenuManager.Hide();
            _stateManager.SetNormal();
        }
        else
        {
            BlockEditorLogger.Log($"HandleSlashMenuToggle: no state change needed");
        }
    }

    private void HandleEmptyBlockBackspace(string text)
    {
        if (_stateManager == null || _keyboardHandler == null) return;

        var hadContentBefore = !string.IsNullOrWhiteSpace(_stateManager.PreviousText);
        var isEmptyNow = string.IsNullOrWhiteSpace(text);
        var isBackspacePress = _keyboardHandler.WasBackspace;
        
        if (hadContentBefore && isEmptyNow && isBackspacePress)
        {
            _keyboardHandler.LastKey = null;
            Dispatcher.UIThread.Post(() => HandleBackspaceOnEmptyBlock(), DispatcherPriority.Input);
        }
    }

    private void OnEscapePressed()
    {
        if (_slashMenuManager?.IsVisible == true && _stateManager != null)
        {
            _slashMenuManager.Hide();
            _stateManager.SetNormal();
        }
    }

    #endregion

    #region Block Type Conversion

    private void ConvertToBlockType(BlockType blockType)
    {
        SetBlockType(blockType);
    }

    private void SetBlockType(BlockType blockType, System.Collections.Generic.Dictionary<string, object>? meta = null)
    {
        if (_viewModel == null || _stateManager == null) return;

        using (_stateManager.BeginUpdate())
        {
            _viewModel.Type = blockType;
            _viewModel.Content = string.Empty;
            _stateManager.PreviousText = string.Empty;
            _focusManager?.ClearCache(); // Clear cache when type changes
            
            if (meta != null)
            {
                foreach (var kvp in meta)
                {
                    _viewModel.Meta[kvp.Key] = kvp.Value;
                }
            }
        }
    }

    #endregion

    #region Slash Menu Handling

    private void OnCommandSelected(BlockType blockType)
    {
        if (_viewModel == null || _stateManager == null) return;

        _viewModel.Type = blockType;
        _viewModel.Content = string.Empty;
        _stateManager.PreviousText = string.Empty;
        _stateManager.SetNormal();
        _focusManager?.ClearCache();
        
        Dispatcher.UIThread.Post(() => _focusManager?.FocusTextBox(), DispatcherPriority.Loaded);
    }

    private void CommandItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not CommandItem command)
        {
            return;
        }

        _slashMenuManager?.HandleItemClick(command);
        e.Handled = true;
    }

    #endregion

    #region Drag and Drop

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
        e.DragEffects = e.Data.Contains("BlockViewModel") 
            ? DragDropEffects.Move 
            : DragDropEffects.None;
    }

    private void Block_Drop(object? sender, DragEventArgs e)
    {
        if (_viewModel == null) return;
        if (e.Data.Get("BlockViewModel") is not BlockViewModel draggedBlock) return;

        var parent = FindParentBlockEditor();
        if (parent == null) return;

        try
        {
            var draggedIndex = parent.Blocks.IndexOf(draggedBlock);
            var targetIndex = parent.Blocks.IndexOf(_viewModel);

            if (draggedIndex != -1 && targetIndex != -1 && draggedIndex != targetIndex)
            {
                parent.Blocks.Move(draggedIndex, targetIndex);
                
                for (int i = 0; i < parent.Blocks.Count; i++)
                {
                    parent.Blocks[i].Order = i;
                }
                
                parent.NotifyBlocksChanged();
            }
        }
        catch (Exception ex)
        {
            BlockEditorLogger.LogError("Error during drop operation", ex);
        }
    }

    private BlockEditor? FindParentBlockEditor()
    {
        // Use cache if available
        if (_cachedParentEditor != null) return _cachedParentEditor;
        
        // Search visual tree
        var current = this.GetVisualParent();
        while (current != null)
        {
            if (current is BlockEditor blockEditor)
            {
                _cachedParentEditor = blockEditor;
                return blockEditor;
            }
            current = current.GetVisualParent();
        }
        
        // Search logical tree as fallback
        var logicalCurrent = this.GetLogicalParent();
        while (logicalCurrent != null)
        {
            if (logicalCurrent is BlockEditor blockEditor)
            {
                _cachedParentEditor = blockEditor;
                return blockEditor;
            }
            logicalCurrent = logicalCurrent.GetLogicalParent();
        }
        
        return null;
    }

    #endregion
}

public class CommandItem
{
    public string Icon { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public BlockType BlockType { get; set; }
}
