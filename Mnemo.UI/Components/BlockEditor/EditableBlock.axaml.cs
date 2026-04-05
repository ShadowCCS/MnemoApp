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
using Mnemo.Core.Formatting;
using Mnemo.UI.Components.BlockEditor.BlockComponents;
using Mnemo.UI.Components.BlockEditor.FormattingToolbar;
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
    private RichTextEditor? _currentEditor;
    private EventHandler<KeyEventArgs>? _backspaceTunnelHandler;
    private bool _backspaceHandledInTunnel;
    private string? _slashMenuOverlayId;
    private SlashCommandMenu? _currentSlashMenu;
    private string? _formattingToolbarOverlayId;
    private InlineFormattingToolbar? _currentFormattingToolbar;
    private TopLevel? _toolbarPointerTopLevel;

    /// <summary>True while the pointer is over the block chrome (gutter icons visible). Gutter borders stay hit-testable so hover works; handlers gate on this.</summary>
    private bool _blockGutterChromeVisible;

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
        var editor = _currentBlockComponent?.GetRichTextEditor();
        if (editor != null)
        {
            var text = editor.Text;
            var caretIndex = Math.Clamp(editor.CaretIndex, 0, text.Length);
            if (_viewModel.Type == BlockType.Quote
                && QuoteEnterBehavior.TryGetSplitOnEmptyLineEnter(text, caretIndex, out var quoteBody, out var followingText))
            {
                _viewModel.NotifyStructuralChangeStarting();
                _viewModel.Content = quoteBody;
                _viewModel.RequestNewBlock(followingText);
                return;
            }

            var textBefore = text.Substring(0, caretIndex);
            var textAfter = text.Substring(caretIndex);
            _viewModel.NotifyStructuralChangeStarting();
            _viewModel.Content = textBefore;
            _viewModel.RequestNewBlock(textAfter);
        }
        else
        {
            _viewModel.NotifyStructuralChangeStarting();
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
        
        // Close any open overlays
        CloseSlashMenu();
        CloseFormattingToolbar();
    }

    private void OnControlLoaded(object? sender, RoutedEventArgs e)
    {
        WireUpBlockComponent();
        Dispatcher.UIThread.Post(() => WireUpBlockComponent(), DispatcherPriority.Render);
        // Content binding can apply after first layout; run again after a short delay so Runs are synced.
        Dispatcher.UIThread.Post(() => WireUpBlockComponent(), DispatcherPriority.ApplicationIdle);
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

            // Always set DataContext so the component has the VM (converter-created content may not inherit it).
            if (_viewModel != null)
            {
                component.DataContext = _viewModel;
                // Explicitly sync runs so text is visible even if DataContextChanged/SyncFromViewModel ran before DC was set.
                if (component.GetRichTextEditor() is { } rte)
                    rte.Runs = _viewModel.Runs;
            }

            component.TextBoxGotFocus += HandleBlockComponentGotFocus;
            component.TextBoxLostFocus += HandleBlockComponentLostFocus;
            component.TextBoxTextChanged += HandleBlockComponentTextChanged;
            component.TextBoxKeyDown += HandleBlockComponentKeyDown;
            
            var editor = component.GetRichTextEditor();
            if (editor != null)
            {
                _currentEditor = editor;
                _backspaceTunnelHandler = OnBackspaceTunnelKeyDown;
                editor.AddHandler(InputElement.KeyDownEvent, _backspaceTunnelHandler, Avalonia.Interactivity.RoutingStrategies.Tunnel);

                editor.PointerReleased += OnTextBoxPointerReleasedForToolbar;
                editor.PointerMoved += OnTextBoxPointerMovedForToolbar;
                editor.KeyUp += OnTextBoxKeyUpForToolbar;
            }
            else
            {
                _currentEditor = null;
            }
        }
        else
        {
            _currentBlockComponent = null;
            _currentEditor = null;
        }
    }
    
    private void RemoveBackspaceTunnelHandler()
    {
        if (_currentEditor != null)
        {
            if (_backspaceTunnelHandler != null)
                _currentEditor.RemoveHandler(InputElement.KeyDownEvent, _backspaceTunnelHandler);
            _currentEditor.PointerReleased -= OnTextBoxPointerReleasedForToolbar;
            _currentEditor.PointerMoved -= OnTextBoxPointerMovedForToolbar;
            _currentEditor.KeyUp -= OnTextBoxKeyUpForToolbar;
            _currentEditor = null;
            _backspaceTunnelHandler = null;
        }
    }
    
    private void OnBackspaceTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Back || e.Handled || _viewModel == null || sender is not RichTextEditor textBox)
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
            _viewModel.NotifyStructuralChangeStarting();
            _backspaceHandledInTunnel = true;
            e.Handled = true;
            HandleBackspaceOnEmptyBlock();
        }
        else if (_viewModel.Type != BlockType.Text)
        {
            _viewModel.NotifyStructuralChangeStarting();
            _backspaceHandledInTunnel = true;
            e.Handled = true;
            HandleConvertToTextPreservingContent();
        }
        else
        {
            _viewModel.NotifyStructuralChangeStarting();
            _viewModel.Content = text;
            _backspaceHandledInTunnel = true;
            e.Handled = true;
            _viewModel.RequestMergeWithPrevious();
        }
    }
    
    private void HandleBlockComponentGotFocus(object? sender, RichTextEditor editor)
    {
        TextBox_GotFocus(editor, new RoutedEventArgs());
    }
    
    private void HandleBlockComponentLostFocus(object? sender, RoutedEventArgs e)
    {
        TextBox_LostFocus(sender, e);
    }
    
    private void HandleBlockComponentTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is BlockComponentBase component && component.GetRichTextEditor() is RichTextEditor editor)
        {
            TextBox_TextChanged(editor, e);
        }
    }
    
    private void HandleBlockComponentKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is BlockComponentBase component && component.GetRichTextEditor() is RichTextEditor editor)
        {
            TextBox_KeyDown(editor, e);
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
        
        // Wire up block component after data context changes; use Render so ContentControl.Content binding has run
        Dispatcher.UIThread.Post(() => WireUpBlockComponent(), DispatcherPriority.Render);
        
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
        _viewModel.ContentChanged += OnViewModelContentChanged;
        UpdateEditableBlockAlignment();
    }

    private void UnsubscribeFromViewModel()
    {
        if (_viewModel == null) return;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.ContentChanged -= OnViewModelContentChanged;
    }

    private void OnViewModelContentChanged(BlockViewModel sender)
    {
        // Image alignment stored in Meta; re-apply if changed
        if (_viewModel?.Type == BlockType.Image)
            UpdateEditableBlockAlignment();
    }

    /// <summary>
    /// For image blocks, apply horizontal alignment to the entire EditableBlock (not inner content)
    /// so the selection chrome hugs the image while the block itself moves left/center/right.
    /// </summary>
    private void UpdateEditableBlockAlignment()
    {
        if (_viewModel?.Type == BlockType.Image)
        {
            var align = ParseImageAlignFromMeta(_viewModel);
            this.HorizontalAlignment = align;
        }
        else
        {
            this.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private static HorizontalAlignment ParseImageAlignFromMeta(BlockViewModel vm)
    {
        if (!vm.Meta.TryGetValue("imageAlign", out var val) || val == null) return HorizontalAlignment.Left;
        var s = val is string str ? str : val.ToString();
        return s?.Trim().ToLowerInvariant() switch
        {
            "center" => HorizontalAlignment.Center,
            "right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left,
        };
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_stateManager == null || _viewModel == null) return;

        switch (e.PropertyName)
        {
            case nameof(BlockViewModel.Type):
                UpdateEditableBlockAlignment();
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
                    CloseSlashMenu();
                    // Delay toolbar close — if focus moved to a non-focusable toolbar button,
                    // the TextBox will regain focus on the same tick and we should keep the toolbar open.
                    if (_formattingToolbarOverlayId != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (_viewModel != null && !_viewModel.IsFocused)
                                CloseFormattingToolbar();
                        }, DispatcherPriority.Input);
                    }
                }
                break;
                
            case nameof(BlockViewModel.Content):
                if (_stateManager != null)
                {
                    var currentText = _viewModel.Content ?? string.Empty;
                    var currentEditor = _focusManager?.GetCurrentTextBox();
                    var editorText = currentEditor?.Text ?? string.Empty;
                    
                    if (editorText == currentText)
                    {
                        _stateManager.PreviousText = currentText;
                        return;
                    }
                    
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
        
        if (sender is RichTextEditor editor)
        {
            _stateManager.PreviousText = editor.Text;
        }
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null && _stateManager != null)
        {
            if (_formattingToolbarOverlayId != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var editor = _currentBlockComponent?.GetRichTextEditor() ?? _focusManager?.GetCurrentTextBox();
                    if (editor != null && editor.IsFocused) return;
                    if (_currentFormattingToolbar?.IsInteractingWithToolbar == true) return;
                    CompleteLostFocus();
                }, DispatcherPriority.Input);
                return;
            }
            CompleteLostFocus();
        }
    }

    private void CompleteLostFocus()
    {
        if (_viewModel == null || _stateManager == null) return;
        _viewModel.IsFocused = false;
        var parentEditor = FindParentBlockEditor();
        parentEditor?.FlushTypingBatch();
        parentEditor?.NotifyBlocksChanged();
    }

    private void TextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not RichTextEditor editor || _viewModel == null || _stateManager == null)
            return;
        
        if (_stateManager.IsUpdatingFromViewModel)
        {
            BlockEditorLogger.Log($"TextBox_TextChanged: SKIPPED (updating from VM) blockId={_viewModel.Id}");
            return;
        }
        
        var text = editor.Text;
        var previousText = _stateManager.PreviousText;
        
        HandleSlashMenuToggle(text, editor);
        
        if (text == previousText)
            return;

        // RichTextEditor: component already committed via CommitRunsFromEditor; just keep state in sync
        if (sender is RichTextEditor)
        {
            _stateManager.PreviousText = text;
            return;
        }

        _viewModel.PreviousContent = previousText;
        var parentEditor = FindParentBlockEditor();
        parentEditor?.TrackTypingEdit(_viewModel, previousText);

        BlockEditorLogger.Log($"TextBox_TextChanged: blockId={_viewModel.Id} prev='{previousText}' -> new='{text}'");
        _viewModel.Content = text;
        _stateManager.PreviousText = text;
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel == null || sender is not RichTextEditor editor || _keyboardHandler == null)
            return;

        if (e.Key == Key.Back && _backspaceHandledInTunnel)
        {
            _backspaceHandledInTunnel = false;
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Back)
            _backspaceHandledInTunnel = false;

        if (e.Key == Key.Space)
            _markdownDetector?.TryDetectShortcut(editor);

        var isSlashKey = e.Key == Key.Divide || e.Key == Key.OemQuestion || e.Key == Key.Oem2;
        if (isSlashKey && string.IsNullOrWhiteSpace(editor.Text) && _stateManager != null && _slashMenuOverlayId == null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var currentText = editor.Text;
                if (currentText == "/" && _slashMenuOverlayId == null)
                {
                    ShowSlashMenu(editor, currentText);
                    _stateManager?.SetShowingSlashMenu();
                }
            }, DispatcherPriority.Input);
        }

        if (e.Key == Key.Enter && _slashMenuOverlayId != null && _currentSlashMenu != null)
        {
            _currentSlashMenu.HandleEnter();
            e.Handled = true;
            return;
        }

        if (_slashMenuOverlayId != null && _currentSlashMenu != null)
        {
            if (e.Key == Key.Up) { _currentSlashMenu.HandleUp(); e.Handled = true; return; }
            if (e.Key == Key.Down) { _currentSlashMenu.HandleDown(); e.Handled = true; return; }
        }

        if (_slashMenuOverlayId != null && !isSlashKey && e.Key != Key.Escape && e.Key != Key.Enter && e.Key != Key.Up && e.Key != Key.Down)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (editor.Text != "/" && _stateManager != null)
                {
                    CloseSlashMenu();
                    _stateManager.SetNormal();
                }
            }, DispatcherPriority.Input);
        }

        _keyboardHandler.HandleKeyDown(e, editor, _viewModel);
    }

    #endregion

    #region Keyboard and Input Handling

    private void HandleBackspaceOnEmptyBlock()
    {
        if (_keyboardHandler == null || _viewModel == null) return;
        _keyboardHandler.HandleBackspaceOnEmptyBlock(_viewModel);
    }

    private void HandleSlashMenuToggle(string text, RichTextEditor textBox)
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

        var content = _currentBlockComponent?.GetRichTextEditor()?.Text ?? _viewModel.Content;

        _viewModel.NotifyStructuralChangeStarting();
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

        _viewModel.NotifyStructuralChangeStarting();

        using (_stateManager.BeginUpdate())
        {
            _viewModel.Type = blockType;
            _viewModel.Content = string.Empty;
            _stateManager.PreviousText = string.Empty;
            _focusManager?.ClearCache();
            
            if (meta != null)
            {
                foreach (var kvp in meta)
                {
                    _viewModel.Meta[kvp.Key] = kvp.Value;
                }
            }
        }

        var addedBlockBelow = (blockType == BlockType.Divider || blockType == BlockType.Image) && EnsureEditableBlockBelowIfNeeded();

        if (!addedBlockBelow)
        {
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

    private static bool IsEditableBlockType(BlockType type) => type != BlockType.Divider && type != BlockType.Image;

    #endregion

    #region Slash Menu Handling

    /// <summary>Estimated height of the slash menu for viewport space checks.</summary>
    private const double SlashMenuHeightEstimate = 320;

    /// <summary>Returns true to show menu above the textbox, false to show below. Default when both fit is below.</summary>
    private static bool ShouldShowSlashMenuAbove(Control textBox)
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

    private void ShowSlashMenu(RichTextEditor textBox, string filterText = "")
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

        _viewModel.NotifyStructuralChangeStarting();
        _viewModel.Type = blockType;
        _viewModel.Content = string.Empty;
        _stateManager.PreviousText = string.Empty;
        _stateManager.SetNormal();
        _focusManager?.ClearCache();

        // When converting to Divider or Image, ensure there is an editable block below so the user can keep typing
        var addedBlockBelow = (blockType == BlockType.Divider || blockType == BlockType.Image) && EnsureEditableBlockBelowIfNeeded();

        if (!addedBlockBelow)
        {
            _viewModel.IsFocused = true;
            Dispatcher.UIThread.Post(() => _focusManager?.FocusTextBox(), DispatcherPriority.Loaded);
        }
    }

    #endregion

    #region Formatting Toolbar

    private const double FormattingToolbarHeightEstimate = 48;

    private void OnTextBoxPointerReleasedForToolbar(object? sender, PointerReleasedEventArgs e)
    {
        Dispatcher.UIThread.Post(CheckSelectionAndToggleToolbar, DispatcherPriority.Input);
    }

    private void OnTextBoxPointerMovedForToolbar(object? sender, PointerEventArgs e)
    {
        if (sender is not RichTextEditor editor) return;
        if (!e.GetCurrentPoint(editor).Properties.IsLeftButtonPressed) return;
        var start = Math.Min(editor.SelectionStart, editor.SelectionEnd);
        var end = Math.Max(editor.SelectionStart, editor.SelectionEnd);
        if (end <= start) return;
        _cachedSelectionRange = (start, end);
        if (_formattingToolbarOverlayId == null)
            ShowFormattingToolbar();
        else
            UpdateFormattingToolbarState();
    }

    private void OnTextBoxKeyUpForToolbar(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
            e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down ||
            e.Key == Key.Home || e.Key == Key.End)
        {
            Dispatcher.UIThread.Post(CheckSelectionAndToggleToolbar, DispatcherPriority.Input);
        }
    }

    private void CheckSelectionAndToggleToolbar()
    {
        var range = GetSelectionRange();
        if (range != null && range.Value.end > range.Value.start)
        {
            _cachedSelectionRange = range;
            if (_formattingToolbarOverlayId == null)
                ShowFormattingToolbar();
            else
                UpdateFormattingToolbarState();
        }
        else
        {
            _cachedSelectionRange = null;
            CloseFormattingToolbar();
        }
    }

    /// <summary>Called by BlockEditor when selection was set by cross-block drag (editor had capture so TextBox never got PointerReleased and toolbar never opened).</summary>
    public void NotifySelectionChangedByEditor()
    {
        Dispatcher.UIThread.Post(CheckSelectionAndToggleToolbar, DispatcherPriority.Input);
    }

    private void ShowFormattingToolbar()
    {
        var textBox = _currentBlockComponent?.GetRichTextEditor() ?? _focusManager?.GetCurrentTextBox();
        if (_overlayService == null || textBox == null || !textBox.IsVisible) return;

        CloseFormattingToolbar();

        var toolbar = new InlineFormattingToolbar();
        toolbar.FormatRequested += OnFormatRequested;
        toolbar.BackgroundColorRequested += OnBackgroundColorRequested;
        _currentFormattingToolbar = toolbar;

        bool showAbove = ShouldShowToolbarAbove(textBox);
        var options = new OverlayOptions
        {
            ShowBackdrop = false,
            CloseOnOutsideClick = true,
            AnchorOffset = showAbove ? new Thickness(0, -8, 0, 0) : new Thickness(0, 4, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };

        // Position by selection center when available so toolbar appears near selected text (e.g. on right side)
        if (textBox is RichTextEditor rte)
        {
            var selBounds = rte.GetSelectionBounds();
            var topLevel = textBox.FindAncestorOfType<TopLevel>();
            if (selBounds is { } rect && topLevel != null)
            {
                var centerX = rect.X + rect.Width / 2;
                var ptCenter = textBox.TranslatePoint(new Point(centerX, 0), topLevel);
                var ptTop = textBox.TranslatePoint(new Point(0, rect.Top), topLevel);
                var ptBottom = textBox.TranslatePoint(new Point(0, rect.Bottom), topLevel);
                if (ptCenter.HasValue && ptTop.HasValue && ptBottom.HasValue)
                {
                    options.AnchorPointX = ptCenter.Value.X;
                    options.AnchorPointY = showAbove ? ptTop.Value.Y : ptBottom.Value.Y;
                    options.AnchorPosition = showAbove ? AnchorPosition.TopCenter : AnchorPosition.BottomCenter;
                }
            }
        }

        if (!options.AnchorPointX.HasValue)
        {
            options.AnchorControl = textBox;
            options.AnchorPosition = showAbove ? AnchorPosition.TopLeft : AnchorPosition.BottomLeft;
        }

        _formattingToolbarOverlayId = _overlayService.CreateOverlay(toolbar, options, "InlineFormattingToolbar");
        AttachToolbarOutsideClickHandler(textBox);
        Dispatcher.UIThread.Post(UpdateFormattingToolbarState, DispatcherPriority.Loaded);
    }

    private void CloseFormattingToolbar()
    {
        if (!string.IsNullOrEmpty(_formattingToolbarOverlayId) && _overlayService != null)
        {
            _overlayService.CloseOverlay(_formattingToolbarOverlayId);
            _formattingToolbarOverlayId = null;
            _cachedSelectionRange = null;
            DetachToolbarOutsideClickHandler();

            if (_currentFormattingToolbar != null)
            {
                _currentFormattingToolbar.FormatRequested -= OnFormatRequested;
                _currentFormattingToolbar.BackgroundColorRequested -= OnBackgroundColorRequested;
                _currentFormattingToolbar = null;
            }
        }
    }

    private static bool ShouldShowToolbarAbove(Control textBox)
    {
        if (textBox == null || !textBox.IsVisible) return true;

        var scrollViewer = textBox.FindAncestorOfType<ScrollViewer>();
        double anchorTop;

        if (scrollViewer != null && scrollViewer.Content is Visual scrollContent)
        {
            var ptInContent = textBox.TranslatePoint(new Point(0, 0), scrollContent);
            if (!ptInContent.HasValue) return true;
            double visibleTop = scrollViewer.Offset.Y;
            anchorTop = ptInContent.Value.Y;
            return (anchorTop - visibleTop) >= FormattingToolbarHeightEstimate;
        }

        var topLevel = textBox.FindAncestorOfType<TopLevel>();
        if (topLevel == null) return true;
        var ptInWindow = textBox.TranslatePoint(new Point(0, 0), topLevel);
        if (!ptInWindow.HasValue) return true;
        return ptInWindow.Value.Y >= FormattingToolbarHeightEstimate;
    }

    private void UpdateFormattingToolbarState()
    {
        if (_currentFormattingToolbar == null || _cachedSelectionRange == null || _viewModel == null) return;
        var (start, end) = _cachedSelectionRange.Value;
        var state = GetFormatStateForRange(_viewModel.Runs, start, end);
        _currentFormattingToolbar.UpdateFormatState(state.bold, state.italic, state.underline, state.strikethrough, state.highlight, state.backgroundColor);
    }

    private static (bool bold, bool italic, bool underline, bool strikethrough, bool highlight, string? backgroundColor) GetFormatStateForRange(IReadOnlyList<InlineRun> runs, int start, int end)
    {
        if (runs.Count == 0 || start >= end) return (false, false, false, false, false, null);
        bool bold = true, italic = true, underline = true, strikethrough = true, highlight = true;
        string? backgroundColor = null;
        bool anyOverlap = false;
        int pos = 0;
        foreach (var run in runs)
        {
            int runEnd = pos + run.Text.Length;
            if (runEnd <= start || pos >= end) { pos = runEnd; continue; }
            anyOverlap = true;
            if (!run.Style.Bold) bold = false;
            if (!run.Style.Italic) italic = false;
            if (!run.Style.Underline) underline = false;
            if (!run.Style.Strikethrough) strikethrough = false;
            if (run.Style.BackgroundColor == null) highlight = false;
            else if (backgroundColor == null) backgroundColor = run.Style.BackgroundColor;
            else if (backgroundColor != run.Style.BackgroundColor) highlight = false;
            pos = runEnd;
        }
        if (!anyOverlap) return (false, false, false, false, false, null);
        return (bold, italic, underline, strikethrough, highlight, backgroundColor);
    }

    private void OnFormatRequested(InlineFormatKind kind)
    {
        string? color = null;
        if (kind == InlineFormatKind.Highlight && Application.Current?.TryFindResource("InlineHighlightColor", out var res) == true && res is Color c)
            color = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        ApplyInlineFormat(kind, color);
    }

    private void OnBackgroundColorRequested(string hex)
    {
        ApplyInlineFormat(InlineFormatKind.BackgroundColor, hex);
    }

    private (int start, int end)? _cachedSelectionRange;

    private void ApplyInlineFormat(InlineFormatKind kind, string? color = null)
    {
        var blockEditor = FindParentBlockEditor();
        if (blockEditor != null && blockEditor.HasCrossBlockTextSelection())
        {
            blockEditor.ApplyInlineFormatToCrossBlockSelection(kind, color);
            return;
        }

        ApplyInlineFormatInternal(kind, color);
    }

    internal void ApplyInlineFormatInternal(InlineFormatKind kind, string? color = null)
    {
        var editor = _currentBlockComponent?.GetRichTextEditor() ?? _focusManager?.GetCurrentTextBox();
        if (editor == null || _viewModel == null) return;

        var range = GetSelectionRange() ?? _cachedSelectionRange;
        if (range == null) return;

        string previousText = _viewModel.Content;

        var (newSelStart, newSelEnd) = _viewModel.ApplyFormat(range.Value.start, range.Value.end, kind, color);

        _stateManager?.SetUpdatingFromViewModel();

        // Sync runs from VM into editor
        editor.Runs = _viewModel.Runs;
        editor.SelectionStart = newSelStart;
        editor.SelectionEnd = newSelEnd;
        editor.CaretIndex = newSelEnd;

        if (_stateManager != null)
            _stateManager.PreviousText = _viewModel.Content;

        _stateManager?.SetNormal();

        _cachedSelectionRange = (newSelStart, newSelEnd);

        UpdateFormattingToolbarState();
        editor.Focus();
    }

    private void AttachToolbarOutsideClickHandler(Control anchorTextBox)
    {
        DetachToolbarOutsideClickHandler();
        _toolbarPointerTopLevel = TopLevel.GetTopLevel(anchorTextBox);
        _toolbarPointerTopLevel?.AddHandler(InputElement.PointerPressedEvent, OnTopLevelPointerPressedForFormattingToolbar, RoutingStrategies.Tunnel);
    }

    private void DetachToolbarOutsideClickHandler()
    {
        if (_toolbarPointerTopLevel != null)
        {
            _toolbarPointerTopLevel.RemoveHandler(InputElement.PointerPressedEvent, OnTopLevelPointerPressedForFormattingToolbar);
            _toolbarPointerTopLevel = null;
        }
    }

    private void OnTopLevelPointerPressedForFormattingToolbar(object? sender, PointerPressedEventArgs e)
    {
        if (_formattingToolbarOverlayId == null || _currentFormattingToolbar == null)
            return;

        if (_currentFormattingToolbar.IsEventFromToolbarOverlay(e.Source))
            return;

        var editor = _currentBlockComponent?.GetRichTextEditor() ?? _focusManager?.GetCurrentTextBox();
        if (editor != null && e.Source is Visual sourceVisual && IsDescendantOf(sourceVisual, editor))
            return;

        CloseFormattingToolbar();
    }

    private static bool IsDescendantOf(Visual source, Visual ancestor)
    {
        Visual? current = source;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
            current = current.GetVisualParent();
        }

        return false;
    }

    #endregion

    #region Drag and Drop

    /// <summary>Root visual captured for block-reorder drag ghost (handle + content).</summary>
    internal Visual BlockDragSnapshotTarget => BlockContainer;

    private void BlockContainer_PointerEntered(object? sender, PointerEventArgs e)
    {
        SetBlockGutterChromeVisible(true);
    }

    private void BlockContainer_PointerExited(object? sender, PointerEventArgs e)
    {
        SetBlockGutterChromeVisible(false);
    }

    private void SetBlockGutterChromeVisible(bool visible)
    {
        _blockGutterChromeVisible = visible;

        // Opacity on the Borders breaks hit-testing in Avalonia; fade only the glyphs.
        if (AddBlockBelowIcon != null)
            AddBlockBelowIcon.Opacity = visible ? 1 : 0;
        if (DragHandleGripPath != null)
            DragHandleGripPath.Opacity = visible ? 0.4 : 0;

        if (!visible)
        {
            ClearGutterHoverBackground(AddBlockBelowBorder);
            ClearGutterHoverBackground(DragHandleBorder);
        }

        InvalidateGutterChrome();
    }

    /// <summary>Forces redraw after glyph opacity changes (avoids stale pixels when moving between blocks).</summary>
    private void InvalidateGutterChrome()
    {
        AddBlockBelowBorder?.InvalidateVisual();
        DragHandleBorder?.InvalidateVisual();
        AddBlockBelowIcon?.InvalidateVisual();
        DragHandleGripPath?.InvalidateVisual();
        InvalidateVisual();
    }

    private void BlockGutterBorder_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (!_blockGutterChromeVisible || sender is not Border border) return;
        if (Application.Current?.TryFindResource("ListItemHoverBackgroundBrush", out var res) == true && res is IBrush brush)
            border.Background = brush;
    }

    private void BlockGutterBorder_PointerExited(object? sender, PointerEventArgs e)
    {
        ClearGutterHoverBackground(sender as Border);
    }

    private static void ClearGutterHoverBackground(Border? border)
    {
        if (border != null)
            border.Background = Brushes.Transparent;
    }

    private void AddBlockBelow_Tapped(object? sender, TappedEventArgs e)
    {
        if (!_blockGutterChromeVisible || _viewModel == null) return;
        e.Handled = true;
        _viewModel.NotifyStructuralChangeStarting();
        _viewModel.RequestNewBlock();
    }

    private async void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_blockGutterChromeVisible || _viewModel == null) return;

        // Clear block selection when user starts dragging to reorder
        var editor = FindParentBlockEditor();
        editor?.ClearBlockSelection();

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(BlockViewModel.BlockDragDataFormat, _viewModel));
        editor?.BeginBlockDragGhost(this, e);
        try
        {
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }
        finally
        {
            editor?.EndBlockDragGhost();
        }
    }

    private void Block_DragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(BlockViewModel.BlockDragDataFormat))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var parent = FindParentBlockEditor();
        Point cursorInEditor = parent != null ? e.GetPosition(parent) : default;
        if (parent != null)
            parent.UpdateBlockDragGhostFromEditorPoint(cursorInEditor);

        if (e.DataTransfer.TryGetValue(BlockViewModel.BlockDragDataFormat) is not { } draggedBlock || draggedBlock == _viewModel)
        {
            e.DragEffects = DragDropEffects.Move;
            return;
        }
        e.DragEffects = DragDropEffects.Move;

        if (parent == null) return;

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
        if (e.DataTransfer.TryGetValue(BlockViewModel.BlockDragDataFormat) is not { } draggedBlock) return;

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

    private RichTextEditor? GetEditor() =>
        _currentBlockComponent?.GetRichTextEditor() ?? _focusManager?.GetCurrentTextBox();

    /// <summary>Live rich-text control when this block is not using a plain <see cref="TextBox"/>.</summary>
    public RichTextEditor? TryGetRichTextEditor() => GetEditor() as RichTextEditor;

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

    public (int start, int end)? GetSelectionRange()
    {
        var editor = GetEditor();
        if (editor == null) return null;
        int start = Math.Min(editor.SelectionStart, editor.SelectionEnd);
        int end = Math.Max(editor.SelectionStart, editor.SelectionEnd);
        if (start >= end) return null;
        return (start, end);
    }

    public int? GetCaretIndex()
    {
        var editor = GetEditor();
        return editor?.CaretIndex;
    }

    public (int start, int end)? GetSelectionOrCaretRange()
    {
        var editor = GetEditor();
        if (editor == null) return null;
        int start = Math.Min(editor.SelectionStart, editor.SelectionEnd);
        int end = Math.Max(editor.SelectionStart, editor.SelectionEnd);
        return (start, end);
    }

    public void SetCaretIndex(int index)
    {
        var editor = GetEditor();
        if (editor == null) return;
        int c = Math.Clamp(index, 0, editor.TextLength);
        editor.CaretIndex = c;
        editor.SelectionStart = c;
        editor.SelectionEnd = c;
    }

    public bool DeleteSelection()
    {
        var range = GetSelectionRange();
        if (range == null || _viewModel == null) return false;
        var editor = GetEditor();
        if (editor == null) return false;
        string text = editor.Text;
        int start = range.Value.start;
        int len = range.Value.end - start;
        if (len <= 0 || start < 0 || start + len > text.Length) return false;
        // Let RichTextEditor handle the run-aware deletion
        editor.SelectionStart = start;
        editor.SelectionEnd = start + len;
        // Trigger deletion by invoking the internal delete logic via key simulation is complex;
        // instead manipulate runs directly through InlineRunFormatApplier
        var newRuns = Core.Formatting.InlineRunFormatApplier.ApplyTextEdit(
            editor.Runs, text, text.Remove(start, len));
        editor.Runs = newRuns;
        editor.CaretIndex = start;
        editor.SelectionStart = start;
        editor.SelectionEnd = start;
        _viewModel.CommitRunsFromEditor(editor.Runs);
        return true;
    }

    public bool InsertTextAtCursor(string text)
    {
        var editor = GetEditor();
        if (editor == null || _viewModel == null) return false;
        int start = Math.Min(editor.SelectionStart, editor.SelectionEnd);
        int end = Math.Max(editor.SelectionStart, editor.SelectionEnd);
        var flat = editor.Text;
        int len = flat.Length;
        start = Math.Clamp(start, 0, len);
        end = Math.Clamp(end, 0, len);
        var newFlat = flat.Remove(start, end - start).Insert(start, text);
        var newRuns = Core.Formatting.InlineRunFormatApplier.ApplyTextEdit(editor.Runs, flat, newFlat);
        editor.Runs = newRuns;
        int newCaret = start + text.Length;
        editor.CaretIndex = newCaret;
        editor.SelectionStart = newCaret;
        editor.SelectionEnd = newCaret;
        _viewModel.CommitRunsFromEditor(editor.Runs);
        return true;
    }

    public void ApplyTextSelection(int start, int end)
    {
        var editor = GetEditor();
        if (editor == null) return;
        var len = editor.TextLength;
        int selStart = Math.Clamp(Math.Min(start, end), 0, len);
        int selEnd = Math.Clamp(Math.Max(start, end), 0, len);

        bool isClear = selStart == 0 && selEnd == 0;
        bool alreadyClear = editor.SelectionStart == 0 && editor.SelectionEnd == 0;
        // Don't skip based on editor.IsFocused: when clearing cross-block selection the "other" block
        // may still have keyboard focus, and we must clear it so selection reliably breaks.
        if (isClear && alreadyClear) return;

        editor.SelectionStart = selStart;
        editor.SelectionEnd = selEnd;
    }

    /// <summary>
    /// For <see cref="BlockEditor"/> pointer tunneling: whether <paramref name="pointInThis"/> (in this control's coordinates)
    /// lies on the block's interactive surface. Image blocks use the drag handle + content chrome only so horizontal gutters
    /// beside a narrow image do not count — box-select and similar gestures can start there.
    /// </summary>
    public bool IsPointerHitInsideBlock(Point pointInThis)
    {
        if (_viewModel?.Type != BlockType.Image)
            return new Rect(0, 0, Bounds.Width, Bounds.Height).Contains(pointInThis);

        if (HitTestImageBlockTarget(AddBlockBelowBorder, pointInThis))
            return true;
        if (HitTestImageBlockTarget(DragHandleBorder, pointInThis))
            return true;
        if (HitTestImageBlockTarget(BlockContentControl, pointInThis))
            return true;
        return false;
    }

    private bool HitTestImageBlockTarget(Control? child, Point pointInThis)
    {
        if (child == null) return false;
        var topLeft = child.TranslatePoint(new Point(0, 0), this);
        if (!topLeft.HasValue) return false;
        var rect = new Rect(topLeft.Value, child.Bounds.Size);
        return rect.Contains(pointInThis);
    }

    /// <summary>
    /// Axis-aligned bounds in <paramref name="relativeTo"/>'s space for box-selection intersection.
    /// Image blocks use the union of handle + content (not the full row width).
    /// </summary>
    public Rect GetBoxSelectIntersectionBoundsRelativeTo(Visual relativeTo)
    {
        if (_viewModel?.Type != BlockType.Image)
        {
            var topLeft = this.TranslatePoint(new Point(0, 0), relativeTo);
            if (!topLeft.HasValue) return default;
            return new Rect(topLeft.Value, Bounds.Size);
        }

        Rect? union = null;
        void Add(Control? c)
        {
            if (c == null) return;
            var tl = c.TranslatePoint(new Point(0, 0), relativeTo);
            if (!tl.HasValue) return;
            var r = new Rect(tl.Value, c.Bounds.Size);
            union = union.HasValue ? union.Value.Union(r) : r;
        }
        Add(AddBlockBelowBorder);
        Add(DragHandleBorder);
        Add(BlockContentControl);
        return union ?? default;
    }

    public int GetCharacterIndexFromPoint(Point pointInBlock)
    {
        var editor = GetEditor();
        if (editor == null) return 0;
        var ptInEditor = this.TranslatePoint(pointInBlock, editor);
        if (!ptInEditor.HasValue) return 0;
        return editor.HitTestPoint(ptInEditor.Value);
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

