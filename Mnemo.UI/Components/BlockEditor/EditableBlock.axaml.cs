using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core;
using Mnemo.Core.Services;
using Mnemo.Core.Models;
using Mnemo.UI.Components.BlockEditor.BlockComponents;
using System;
using System.Linq;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>
/// Improved EditableBlock with better separation of concerns,
/// improved performance through caching, and cleaner architecture.
/// </summary>
public partial class EditableBlock : UserControl
{
    // Manager instances
    private KeyboardHandler? _keyboardHandler;
    private FocusManager? _focusManager;
    private MarkdownShortcutDetector? _markdownDetector;
    private EditorStateManager? _stateManager;
    private IOverlayService? _overlayService;
    
    private BlockViewModel? _viewModel;
    private BlockEditor? _cachedParentEditor;
    private BlockComponentBase? _currentBlockComponent;
    private string? _slashMenuOverlayId;
    private SlashCommandMenu? _currentSlashMenu;

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
        _overlayService = ((App)Application.Current!).Services!.GetService<IOverlayService>();

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
        
        // Unwire block component
        if (_currentBlockComponent != null)
        {
            _currentBlockComponent.TextBoxGotFocus -= HandleBlockComponentGotFocus;
            _currentBlockComponent.TextBoxLostFocus -= HandleBlockComponentLostFocus;
            _currentBlockComponent.TextBoxTextChanged -= HandleBlockComponentTextChanged;
            _currentBlockComponent.TextBoxKeyDown -= HandleBlockComponentKeyDown;
            _currentBlockComponent = null;
        }
        
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
        
        // Close any open slash menu overlay
        CloseSlashMenu();
    }

    private void OnControlLoaded(object? sender, RoutedEventArgs e)
    {
        // Wire up block component events
        WireUpBlockComponent();
    }
    
    private void WireUpBlockComponent()
    {
        // Unwire previous component
        if (_currentBlockComponent != null)
        {
            _currentBlockComponent.TextBoxGotFocus -= HandleBlockComponentGotFocus;
            _currentBlockComponent.TextBoxLostFocus -= HandleBlockComponentLostFocus;
            _currentBlockComponent.TextBoxTextChanged -= HandleBlockComponentTextChanged;
            _currentBlockComponent.TextBoxKeyDown -= HandleBlockComponentKeyDown;
        }
        
        // Find and wire up new component
        if (BlockContentControl?.Content is BlockComponentBase component)
        {
            _currentBlockComponent = component;
            
            // Ensure DataContext is set on the component
            if (component.DataContext == null && _viewModel != null)
            {
                component.DataContext = _viewModel;
            }
            
            component.TextBoxGotFocus += HandleBlockComponentGotFocus;
            component.TextBoxLostFocus += HandleBlockComponentLostFocus;
            component.TextBoxTextChanged += HandleBlockComponentTextChanged;
            component.TextBoxKeyDown += HandleBlockComponentKeyDown;
        }
        else
        {
            _currentBlockComponent = null;
        }
    }
    
    private void HandleBlockComponentGotFocus(object? sender, TextBox textBox)
    {
        TextBox_GotFocus(textBox, new RoutedEventArgs());
    }
    
    private void HandleBlockComponentLostFocus(object? sender, RoutedEventArgs e)
    {
        TextBox_LostFocus(sender, e);
    }
    
    private void HandleBlockComponentTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is BlockComponentBase component && component.GetInputControl() is TextBox textBox)
        {
            TextBox_TextChanged(textBox, e);
        }
    }
    
    private void HandleBlockComponentKeyDown(object? sender, KeyEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[EditableBlock] HandleBlockComponentKeyDown called - Key: {e.Key}, Sender: {sender?.GetType().Name}");
        if (sender is BlockComponentBase component && component.GetInputControl() is TextBox textBox)
        {
            System.Diagnostics.Debug.WriteLine($"[EditableBlock] Routing to TextBox_KeyDown");
            TextBox_KeyDown(textBox, e);
        }
        else
        {
            var isComponent = sender is BlockComponentBase;
            var component2 = sender as BlockComponentBase;
            var hasTextBox = component2?.GetInputControl() is TextBox;
            System.Diagnostics.Debug.WriteLine($"[EditableBlock] Failed to get TextBox - IsComponent: {isComponent}, HasTextBox: {hasTextBox}");
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
        
        // Wire up block component after data context changes
        Dispatcher.UIThread.Post(() => WireUpBlockComponent(), DispatcherPriority.Loaded);
        
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
            case nameof(BlockViewModel.Type):
                // Block type changed, need to rewire component
                Dispatcher.UIThread.Post(() => WireUpBlockComponent(), DispatcherPriority.Loaded);
                break;
                
            case nameof(BlockViewModel.IsFocused):
                if (_viewModel.IsFocused)
                {
                    _focusManager?.ClearCache(); // Clear cache when focus changes
                    _focusManager?.FocusTextBox();
                }
                else
                {
                    // Hide slash menu when block loses focus
                    CloseSlashMenu();
                }
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
        if (sender is not TextBox textBox || _viewModel == null || _stateManager == null)
        {
            return;
        }
        
        if (_stateManager.IsUpdatingFromViewModel)
        {
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
            return;
        }
        
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
        System.Diagnostics.Debug.WriteLine($"[EditableBlock] TextBox_KeyDown called - Key: {e.Key}, Handled: {e.Handled}, Sender: {sender?.GetType().Name}");
        
        if (_viewModel == null || sender is not TextBox textBox || _keyboardHandler == null)
        {
            System.Diagnostics.Debug.WriteLine($"[EditableBlock] Early return - ViewModel: {_viewModel != null}, TextBox: {sender is TextBox}, Handler: {_keyboardHandler != null}");
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
        if (isSlashKey && string.IsNullOrWhiteSpace(textBox.Text) && _stateManager != null && _slashMenuOverlayId == null)
        {
            // Text will become "/" after this keypress - show menu optimistically for immediate feedback
            // TextChanged will validate this and correct if needed
            Dispatcher.UIThread.Post(() =>
            {
                var currentText = textBox.Text ?? string.Empty;
                if (currentText == "/" && _slashMenuOverlayId == null)
                {
                    ShowSlashMenu(textBox, currentText);
                    _stateManager?.SetShowingSlashMenu();
                }
            }, DispatcherPriority.Input);
        }

        // Handle Enter key when slash menu is visible
        if (e.Key == Key.Enter && _slashMenuOverlayId != null && _currentSlashMenu != null)
        {
            _currentSlashMenu.HandleEnter();
            e.Handled = true;
            return;
        }

        // Fallback: Hide menu if visible and any non-slash key is pressed (covers cases where TextChanged doesn't fire)
        // This ensures menu state is always correct regardless of event timing
        if (_slashMenuOverlayId != null && !isSlashKey && e.Key != Key.Escape && e.Key != Key.Enter && e.Key != Key.Up && e.Key != Key.Down)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var currentText = textBox.Text ?? string.Empty;
                if (currentText != "/" && _stateManager != null)
                {
                    CloseSlashMenu();
                    _stateManager.SetNormal();
                }
            }, DispatcherPriority.Input);
        }

        // Handle backspace on empty block BEFORE delegating to keyboard handler
        // This ensures we catch it before TextBox processes it
        if (e.Key == Key.Back && _viewModel != null && !e.Handled)
        {
            var text = textBox.Text ?? string.Empty;
            var caretIndex = textBox.CaretIndex;
            var selectionLength = Math.Abs(textBox.SelectionEnd - textBox.SelectionStart);
            
            System.Diagnostics.Debug.WriteLine($"[EditableBlock] Backspace detected - Text: '{text}', CaretIndex: {caretIndex}, SelectionLength: {selectionLength}, IsEmpty: {string.IsNullOrWhiteSpace(text)}, BlockType: {_viewModel.Type}");
            
            // If at start of block with no selection and block is empty
            if (caretIndex == 0 && selectionLength == 0 && string.IsNullOrWhiteSpace(text))
            {
                System.Diagnostics.Debug.WriteLine($"[EditableBlock] Handling backspace on empty block - Type: {_viewModel.Type}");
                e.Handled = true;
                HandleBackspaceOnEmptyBlock();
                return;
            }
        }

        // Delegate to keyboard handler
        _keyboardHandler.HandleKeyDown(e, textBox, _viewModel);
    }

    #endregion

    #region Keyboard and Input Handling

    private void HandleBackspaceOnEmptyBlock()
    {
        System.Diagnostics.Debug.WriteLine($"[EditableBlock] HandleBackspaceOnEmptyBlock called - ViewModel: {_viewModel != null}, Handler: {_keyboardHandler != null}");
        if (_keyboardHandler == null || _viewModel == null) return;
        System.Diagnostics.Debug.WriteLine($"[EditableBlock] Calling keyboard handler - BlockType: {_viewModel.Type}");
        _keyboardHandler.HandleBackspaceOnEmptyBlock(_viewModel);
    }

    private void HandleSlashMenuToggle(string text, TextBox textBox)
    {
        if (_stateManager == null)
        {
            return;
        }

        // Check if text starts with "/" and is a valid slash command pattern
        var isSlashCommand = text.StartsWith("/");
        var menuIsVisible = _slashMenuOverlayId != null;

        // Always ensure state matches reality - correct any mismatches
        if (isSlashCommand && !menuIsVisible)
        {
            ShowSlashMenu(textBox, text);
            _stateManager.SetShowingSlashMenu();
        }
        else if (isSlashCommand && menuIsVisible && _currentSlashMenu != null)
        {
            // Update filter if menu is already visible
            _currentSlashMenu.UpdateFilter(text);
        }
        else if (!isSlashCommand && menuIsVisible)
        {
            CloseSlashMenu();
            _stateManager.SetNormal();
        }
    }


    private void OnEscapePressed()
    {
        if (_slashMenuOverlayId != null && _stateManager != null)
        {
            CloseSlashMenu();
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

    private void ShowSlashMenu(TextBox textBox, string filterText = "")
    {
        if (_overlayService == null || textBox == null || !textBox.IsVisible) return;

        // Close existing menu if any
        CloseSlashMenu();

        var menu = new SlashCommandMenu();
        menu.CommandSelected += OnCommandSelected;
        menu.UpdateFilter(filterText);
        _currentSlashMenu = menu;

        var options = new OverlayOptions
        {
            ShowBackdrop = false,
            CloseOnOutsideClick = true,
            AnchorControl = textBox,
            AnchorPosition = AnchorPosition.BottomLeft,
            AnchorOffset = new Thickness(0, 4, 0, 0), // 4px offset below textbox
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };

        _slashMenuOverlayId = _overlayService.CreateOverlay(menu, options, "SlashCommandMenu");
    }

    private void CloseSlashMenu()
    {
        if (!string.IsNullOrEmpty(_slashMenuOverlayId) && _overlayService != null)
        {
            _overlayService.CloseOverlay(_slashMenuOverlayId);
            _slashMenuOverlayId = null;
            _currentSlashMenu = null;
        }
    }

    private void OnCommandSelected(BlockType blockType)
    {
        if (_viewModel == null || _stateManager == null) return;

        CloseSlashMenu();

        _viewModel.Type = blockType;
        _viewModel.Content = string.Empty;
        _stateManager.PreviousText = string.Empty;
        _stateManager.SetNormal();
        _focusManager?.ClearCache();
        
        Dispatcher.UIThread.Post(() => _focusManager?.FocusTextBox(), DispatcherPriority.Loaded);
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

