using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
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
    private TextBox? _currentTunnelTextBox;
    private EventHandler<KeyEventArgs>? _backspaceTunnelHandler;
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
        _keyboardHandler.ConvertToTextPreservingContent += HandleConvertToTextPreservingContent;
        _keyboardHandler.EscapePressed += OnEscapePressed;
        _keyboardHandler.MergeWithPrevious += HandleMergeWithPrevious;

        // Wire up markdown detector
        _markdownDetector.ShortcutDetected += OnMarkdownShortcutDetected;
    }

    private void HandleRequestFocusPrevious() => _viewModel?.RequestFocusPrevious();
    private void HandleRequestFocusNext() => _viewModel?.RequestFocusNext();
    private void HandleMergeWithPrevious() => _viewModel?.RequestMergeWithPrevious();
    private void HandleEnterPressed()
    {
        if (_viewModel == null) return;
        var input = _currentBlockComponent?.GetInputControl();
        if (input is TextBox textBox)
        {
            var text = textBox.Text ?? string.Empty;
            var caretIndex = Math.Clamp(textBox.CaretIndex, 0, text.Length);
            var textBefore = text.Substring(0, caretIndex);
            var textAfter = text.Substring(caretIndex);
            _viewModel.Content = textBefore;
            _viewModel.RequestNewBlock(textAfter);
        }
        else
        {
            _viewModel.RequestNewBlock();
        }
    }
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
            RemoveBackspaceTunnelHandler();
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
            _keyboardHandler.ConvertToTextPreservingContent -= HandleConvertToTextPreservingContent;
            _keyboardHandler.EscapePressed -= OnEscapePressed;
            _keyboardHandler.MergeWithPrevious -= HandleMergeWithPrevious;
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
            RemoveBackspaceTunnelHandler();
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
            
            // Handle backspace on empty in tunnel phase so we run before TextBox consumes the key
            if (component.GetInputControl() is TextBox textBox)
            {
                _backspaceTunnelHandler = OnBackspaceTunnelKeyDown;
                textBox.AddHandler(InputElement.KeyDownEvent, _backspaceTunnelHandler, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                _currentTunnelTextBox = textBox;
            }
            else
            {
                _currentTunnelTextBox = null;
            }
        }
        else
        {
            _currentBlockComponent = null;
            _currentTunnelTextBox = null;
        }
    }
    
    private void RemoveBackspaceTunnelHandler()
    {
        if (_currentTunnelTextBox != null && _backspaceTunnelHandler != null)
        {
            _currentTunnelTextBox.RemoveHandler(InputElement.KeyDownEvent, _backspaceTunnelHandler);
            _currentTunnelTextBox = null;
            _backspaceTunnelHandler = null;
        }
    }
    
    private void OnBackspaceTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Back || e.Handled || _viewModel == null || sender is not TextBox textBox)
            return;
        var text = textBox.Text ?? string.Empty;
        var caretIndex = textBox.CaretIndex;
        var selectionLength = Math.Abs(textBox.SelectionEnd - textBox.SelectionStart);

        // Only intercept when caret is at position 0 with no selection
        if (caretIndex != 0 || selectionLength != 0)
            return;

        // Use the live TextBox text — _viewModel.Content may not have synced yet at KeyDown time
        var isEmpty = string.IsNullOrWhiteSpace(text);

        if (isEmpty)
        {
            // Empty block: delete/convert
            e.Handled = true;
            HandleBackspaceOnEmptyBlock();
        }
        else if (_viewModel.Type != BlockType.Text)
        {
            // Non-text block with content at position 0: convert to Text, preserving content
            e.Handled = true;
            HandleConvertToTextPreservingContent();
        }
        else
        {
            // Text block with content at position 0: merge with the block above.
            // Sync TextBox content to ViewModel first — TextChanged hasn't fired yet at KeyDown time.
            _viewModel.Content = text;
            e.Handled = true;
            _viewModel.RequestMergeWithPrevious();
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
        if (sender is BlockComponentBase component && component.GetInputControl() is TextBox textBox)
        {
            TextBox_KeyDown(textBox, e);
        }
    }

    private void SetupDragDrop()
    {
        if (BlockContainer == null) return;

        DragDrop.SetAllowDrop(BlockContainer, true);
        BlockContainer.AddHandler(DragDrop.DragOverEvent, Block_DragOver);
        BlockContainer.AddHandler(DragDrop.DropEvent, Block_Drop);
        BlockContainer.AddHandler(DragDrop.DragLeaveEvent, Block_DragLeave);
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
                    _focusManager?.ClearCache();

                    // During a cross-block selection drag the editor owns focus — calling FocusTextBox()
                    // here would steal focus away from the anchor block and cause flicker/thrashing.
                    var parentEditor = FindParentBlockEditor();
                    if (parentEditor?.IsCrossBlockSelectingActive == true) break;

                    // Only programmatically focus+move caret when the TextBox isn't already focused.
                    // If the user clicked the TextBox directly, it already has focus and the correct
                    // caret position — calling FocusTextBox() would snap the caret to the end.
                    var alreadyFocused = _focusManager?.GetFocusedTextBox() != null;

                    if (_viewModel.PendingCaretIndex.HasValue)
                    {
                        var caretIndex = _viewModel.PendingCaretIndex.Value;
                        _viewModel.PendingCaretIndex = null;
                        _focusManager?.FocusTextBox(caretIndex);
                    }
                    else if (!alreadyFocused)
                    {
                        _focusManager?.FocusTextBox();
                    }
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
        if (_viewModel == null || sender is not TextBox textBox || _keyboardHandler == null)
            return;

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

        // Handle Up/Down when slash menu is visible for keyboard navigation
        if (_slashMenuOverlayId != null && _currentSlashMenu != null)
        {
            if (e.Key == Key.Up)
            {
                _currentSlashMenu.HandleUp();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Down)
            {
                _currentSlashMenu.HandleDown();
                e.Handled = true;
                return;
            }
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

    private void HandleConvertToTextPreservingContent()
    {
        if (_viewModel == null || _stateManager == null) return;

        // Read content from the live TextBox before the component is swapped out
        var textBox = _currentBlockComponent?.GetInputControl() as TextBox;
        var content = textBox?.Text ?? _viewModel.Content;

        // Change type — this causes the ContentControl to swap to a new TextBlockComponent.
        // We use BeginUpdate so TextChanged on the new component doesn't misfire.
        _stateManager.SetUpdatingFromViewModel();
        _viewModel.Type = BlockType.Text;

        // After the new TextBlockComponent is wired up (WireUpBlockComponent posts at Loaded),
        // restore content and focus in a single post at Render priority so layout has settled
        // but we are still before normal Input processing.
        Dispatcher.UIThread.Post(() =>
        {
            _stateManager.PreviousText = content;
            _viewModel.Content = content;
            _stateManager.SetNormal();
            _focusManager?.ClearCache();
            _focusManager?.FocusTextBox(0);
        }, DispatcherPriority.Render);
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

        // When converting to a non-editable block (e.g. Divider), ensure there is an editable block below so the user can keep typing
        var addedBlockBelow = blockType == BlockType.Divider && EnsureEditableBlockBelowIfNeeded();

        if (!addedBlockBelow)
        {
            // Keep focus on this block after conversion (explicit Post needed when IsFocused was already true)
            _viewModel.IsFocused = true;
            Dispatcher.UIThread.Post(() => _focusManager?.FocusTextBox(), DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// If the current block is non-editable and there is no editable block below, inserts a new Text block below and requests focus on it.
    /// Returns true if a block was added (caller should not focus the current block).
    /// </summary>
    private bool EnsureEditableBlockBelowIfNeeded()
    {
        if (_viewModel == null) return false;

        var editor = FindParentBlockEditor();
        if (editor == null) return false;

        var index = editor.Blocks.IndexOf(_viewModel);
        if (index < 0) return false;

        var nextIndex = index + 1;
        var hasEditableBelow = nextIndex < editor.Blocks.Count && IsEditableBlockType(editor.Blocks[nextIndex].Type);
        if (hasEditableBelow) return false;

        _viewModel.RequestNewBlockOfType(BlockType.Text);
        return true;
    }

    private static bool IsEditableBlockType(BlockType type) => type != BlockType.Divider;

    #endregion

    #region Slash Menu Handling

    /// <summary>Estimated height of the slash menu for viewport space checks.</summary>
    private const double SlashMenuHeightEstimate = 320;

    /// <summary>Returns true to show menu above the textbox, false to show below. Default when both fit is below.</summary>
    private static bool ShouldShowSlashMenuAbove(TextBox textBox)
    {
        if (textBox == null || !textBox.IsVisible) return false;

        var scrollViewer = textBox.FindAncestorOfType<ScrollViewer>();
        double visibleTop;
        double visibleBottom;
        double anchorTop;
        double anchorBottom;

        if (scrollViewer != null && scrollViewer.Content is Visual scrollContent)
        {
            var ptInContent = textBox.TranslatePoint(new Point(0, 0), scrollContent);
            if (!ptInContent.HasValue) return false;
            visibleTop = scrollViewer.Offset.Y;
            visibleBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height;
            anchorTop = ptInContent.Value.Y;
            anchorBottom = ptInContent.Value.Y + textBox.Bounds.Height;
        }
        else
        {
            var topLevel = textBox.FindAncestorOfType<TopLevel>();
            if (topLevel == null) return false;
            var ptInWindow = textBox.TranslatePoint(new Point(0, 0), topLevel);
            if (!ptInWindow.HasValue) return false;
            visibleTop = 0;
            visibleBottom = topLevel.Bounds.Height;
            anchorTop = ptInWindow.Value.Y;
            anchorBottom = ptInWindow.Value.Y + textBox.Bounds.Height;
        }

        double spaceAbove = anchorTop - visibleTop;
        double spaceBelow = visibleBottom - anchorBottom;

        // If not enough space below but enough above → show above
        if (spaceBelow < SlashMenuHeightEstimate && spaceAbove >= spaceBelow)
            return true;
        // If not enough space above but enough below → show below
        if (spaceAbove < SlashMenuHeightEstimate && spaceBelow >= spaceAbove)
            return false;
        // Default: show below when both fit or when both are tight
        return false;
    }

    private void ShowSlashMenu(TextBox textBox, string filterText = "")
    {
        if (_overlayService == null || textBox == null || !textBox.IsVisible) return;

        // Close existing menu if any
        CloseSlashMenu();

        var menu = new SlashCommandMenu();
        menu.CommandSelected += OnCommandSelected;
        menu.UpdateFilter(filterText);
        _currentSlashMenu = menu;

        bool showAbove = ShouldShowSlashMenuAbove(textBox);
        var options = new OverlayOptions
        {
            ShowBackdrop = false,
            CloseOnOutsideClick = true,
            AnchorControl = textBox,
            AnchorPosition = showAbove ? AnchorPosition.TopLeft : AnchorPosition.BottomLeft,
            AnchorOffset = showAbove ? new Thickness(0, -4, 0, 0) : new Thickness(0, 4, 0, 0),
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

        // When converting to Divider, ensure there is an editable block below so the user can keep typing
        var addedBlockBelow = blockType == BlockType.Divider && EnsureEditableBlockBelowIfNeeded();

        if (!addedBlockBelow)
        {
            _viewModel.IsFocused = true;
            Dispatcher.UIThread.Post(() => _focusManager?.FocusTextBox(), DispatcherPriority.Loaded);
        }
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

        // Clear block selection when user starts dragging to reorder
        FindParentBlockEditor()?.ClearBlockSelection();

        var data = new DataObject();
        data.Set("BlockViewModel", _viewModel);

        DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void Block_DragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("BlockViewModel"))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        if (e.Data.Get("BlockViewModel") is not BlockViewModel draggedBlock || draggedBlock == _viewModel)
        {
            e.DragEffects = DragDropEffects.Move;
            return;
        }
        e.DragEffects = DragDropEffects.Move;

        var parent = FindParentBlockEditor();
        if (parent == null) return;

        var cursorInEditor = e.GetPosition(parent);
        parent.HandleBlockDragOver(cursorInEditor, draggedBlock);
    }

    public void ShowDropLineAtTop()
    {
        if (DropIndicatorLine == null) return;
        DropIndicatorLine.VerticalAlignment = VerticalAlignment.Top;
        DropIndicatorLine.IsVisible = true;
    }

    public void ShowDropLineAtBottom()
    {
        if (DropIndicatorLine == null) return;
        DropIndicatorLine.VerticalAlignment = VerticalAlignment.Bottom;
        DropIndicatorLine.IsVisible = true;
    }

    public void HideDropLine()
    {
        if (DropIndicatorLine != null)
            DropIndicatorLine.IsVisible = false;
    }

    private void Block_DragLeave(object? sender, DragEventArgs e)
    {
        // Do not clear the drop indicator here: the pointer may have moved from this block's
        // Border to its inner content (e.g. TextBox), which would cause flicker. The editor
        // clears the indicator when the pointer leaves the editor (Editor_DragLeave).
    }

    private void Block_Drop(object? sender, DragEventArgs e)
    {
        if (_viewModel == null) return;
        if (e.Data.Get("BlockViewModel") is not BlockViewModel draggedBlock) return;

        var parent = FindParentBlockEditor();
        if (parent == null) return;

        try
        {
            if (!parent.TryPerformDrop(draggedBlock))
                BlockEditorLogger.LogError("Drop failed: invalid insert index or block", null);
        }
        catch (Exception ex)
        {
            BlockEditorLogger.LogError("Error during drop operation", ex);
        }
        finally
        {
            parent.ClearDropIndicator();
        }
    }

    /// <summary>
    /// Gets the currently selected text from this block's TextBox, or null if no selection or no TextBox.
    /// </summary>
    public string? GetSelectedText()
    {
        var range = GetSelectionRange();
        if (range == null || _viewModel == null) return null;
        var content = _viewModel.Content ?? string.Empty;
        int start = Math.Clamp(range.Value.start, 0, content.Length);
        int end = Math.Clamp(range.Value.end, 0, content.Length);
        if (start >= end) return null;
        return content.Substring(start, end - start);
    }

    /// <summary>
    /// Gets the (SelectionStart, SelectionEnd) from this block's TextBox, or null if no TextBox.
    /// </summary>
    public (int start, int end)? GetSelectionRange()
    {
        var textBox = _currentBlockComponent?.GetInputControl() as TextBox ?? _focusManager?.GetCurrentTextBox();
        if (textBox?.Text == null) return null;
        int start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        int end = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
        if (start >= end) return null;
        return (start, end);
    }

    /// <summary>
    /// Deletes the currently selected text in this block's TextBox and syncs to the view model.
    /// No-op if there is no selection or no TextBox. Used for Backspace/Delete with text selection (including cross-block).
    /// </summary>
    public bool DeleteSelection()
    {
        var range = GetSelectionRange();
        if (range == null || _viewModel == null) return false;
        var textBox = _currentBlockComponent?.GetInputControl() as TextBox ?? _focusManager?.GetCurrentTextBox();
        if (textBox?.Text == null) return false;
        string text = textBox.Text;
        int start = range.Value.start;
        int len = range.Value.end - start;
        if (len <= 0 || start < 0 || start + len > text.Length) return false;
        string newText = text.Remove(start, len);
        textBox.Text = newText;
        textBox.CaretIndex = start;
        textBox.SelectionStart = start;
        textBox.SelectionEnd = start;
        _viewModel.Content = newText;
        _viewModel.NotifyContentChanged();
        return true;
    }

    /// <summary>
    /// Applies a text selection range to this block's TextBox (for cross-block selection).
    /// No-op if the block has no TextBox (e.g. Divider).
    /// </summary>
    public void ApplyTextSelection(int start, int end)
    {
        var textBox = _currentBlockComponent?.GetInputControl() as TextBox ?? _focusManager?.GetCurrentTextBox();
        if (textBox == null) return;
        var len = textBox.Text?.Length ?? 0;
        int selStart = Math.Clamp(Math.Min(start, end), 0, len);
        int selEnd = Math.Clamp(Math.Max(start, end), 0, len);

        // When clearing selection (selStart == selEnd == 0), skip the assignment in two cases:
        // 1) This TextBox is currently focused — we're in the middle of a focus transfer to another
        //    block; setting SelectionStart/End = 0 here snaps the caret to 0 for one frame (flicker).
        // 2) This TextBox is unfocused and already has no selection — no-op.
        bool isClear = selStart == 0 && selEnd == 0;
        bool alreadyClear = textBox.SelectionStart == 0 && textBox.SelectionEnd == 0;
        if (isClear && (textBox.IsFocused || alreadyClear)) return;

        textBox.SelectionStart = selStart;
        textBox.SelectionEnd = selEnd;
        // Do NOT set CaretIndex here — doing so on an unfocused TextBox causes it to steal
        // keyboard focus, which creates a focus-thrashing loop during cross-block drag selection.
        // SelectionStart/End alone are sufficient to render the highlight.
    }

    /// <summary>
    /// Gets the character index in this block's TextBox closest to the given point (in this control's coordinates).
    /// Uses the TextPresenter's already-rendered TextLayout for a side-effect-free, pixel-accurate hit-test.
    /// </summary>
    public int GetCharacterIndexFromPoint(Point pointInBlock)
    {
        var textBox = _currentBlockComponent?.GetInputControl() as TextBox ?? _focusManager?.GetCurrentTextBox();
        if (textBox == null) return 0;

        var presenter = textBox.GetVisualDescendants()
            .OfType<Avalonia.Controls.Presenters.TextPresenter>()
            .FirstOrDefault();
        if (presenter == null) return 0;

        // Translate into the TextPresenter's coordinate space — this accounts for all TextBox
        // padding so the hit-test uses the exact same origin the rendered glyphs use.
        var ptInPresenter = this.TranslatePoint(pointInBlock, presenter);
        if (!ptInPresenter.HasValue) return 0;

        var len = textBox.Text?.Length ?? 0;
        if (len == 0) return 0;

        try
        {
            var result = presenter.TextLayout.HitTestPoint(ptInPresenter.Value);
            var pos = result.TextPosition;
            if (result.IsTrailing && pos < len)
                pos++;
            return Math.Clamp(pos, 0, len);
        }
        catch
        {
            return 0;
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

