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
using Mnemo.UI.Components.BlockEditor.BlockComponents.Image;
using Mnemo.UI.Components.BlockEditor.FormattingToolbar;
using Mnemo.UI.Components.Overlays;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mnemo.UI;

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
    private IDisposable? _selectionStartSubscription;
    private IDisposable? _selectionEndSubscription;
    private EventHandler<KeyEventArgs>? _backspaceTunnelHandler;
    private bool _backspaceHandledInTunnel;
    private string? _slashMenuOverlayId;
    private SlashCommandMenu? _currentSlashMenu;
    private string? _formattingToolbarOverlayId;
    private InlineFormattingToolbar? _currentFormattingToolbar;
    private TopLevel? _toolbarPointerTopLevel;

    /// <summary>
    /// Tracks sticky sub/sup typing mode. Null = off; Subscript or Superscript = active.
    /// When active, all typed characters receive that format until explicitly toggled off or the caret navigates away.
    /// </summary>
    private InlineFormatKind? _stickySubSup;

    /// <summary>True while the pointer is over the block chrome (gutter icons visible). Gutter borders stay hit-testable so hover works; handlers gate on this.</summary>
    private bool _blockGutterChromeVisible;

    /// <summary>Device-independent px before a press on the drag handle becomes a block reorder drag (matches image chrome).</summary>
    private const double DragHandleReorderDragThresholdPixels = 6;

    private Point? _dragHandleReorderPressPoint;
    private bool _dragHandleReorderDragLaunched;

    /// <summary>Matches gutter <see cref="Border"/> MinHeight in <c>EditableBlock.axaml</c>.</summary>
    private const double GutterChromeHeight = 26;

    private double _gutterMarginTopCache = double.NaN;

    private bool _isNestedInColumn;

    private bool IsReadOnly => FindParentBlockEditor()?.IsReadOnly == true;

    /// <summary>Column cells hide the add/drag gutter; the parent row keeps the handle.</summary>
    public bool IsNestedInColumn
    {
        get => _isNestedInColumn;
        set
        {
            if (_isNestedInColumn == value) return;
            _isNestedInColumn = value;
            ApplyNestedColumnLayout();
        }
    }

    private void ApplyNestedColumnLayout()
    {
        if (BlockLayoutGrid == null) return;
        if (_isNestedInColumn)
        {
            BlockLayoutGrid.ColumnDefinitions[0].Width = new GridLength(0);
            BlockLayoutGrid.ColumnDefinitions[1].Width = new GridLength(0);
            if (AddBlockBelowBorder != null) AddBlockBelowBorder.IsVisible = false;
            if (DragHandleBorder != null) DragHandleBorder.IsVisible = false;
        }
        else
        {
            BlockLayoutGrid.ColumnDefinitions[0].Width = GridLength.Auto;
            BlockLayoutGrid.ColumnDefinitions[1].Width = GridLength.Auto;
            if (AddBlockBelowBorder != null) AddBlockBelowBorder.IsVisible = true;
            if (DragHandleBorder != null) DragHandleBorder.IsVisible = true;
        }
    }

    public EditableBlock()
    {
        InitializeComponent();
        InitializeManagers();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnControlLoaded;
        Unloaded += OnControlUnloaded;
        LayoutUpdated += (_, _) => UpdateGutterVerticalAlignment();

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
        _keyboardHandler.ConvertToBlockType += ConvertToBlockType;
        _keyboardHandler.ConvertToTextPreservingContent += HandleConvertToTextPreservingContent;
        _keyboardHandler.EscapePressed += OnEscapePressed;
        _keyboardHandler.MergeWithPrevious += HandleMergeWithPrevious;

        // Wire up markdown detector
        _markdownDetector.ShortcutDetected += OnMarkdownShortcutDetected;
    }

    private void HandleRequestFocusPrevious(double? caretPixelX) => _viewModel?.RequestFocusPrevious(caretPixelX);
    private void HandleRequestFocusNext(double? caretPixelX) => _viewModel?.RequestFocusNext(caretPixelX);
    private void HandleMergeWithPrevious() => _viewModel?.RequestMergeWithPrevious();
    private void HandleEnterPressed()
    {
        if (_viewModel == null) return;
        var editor = _currentBlockComponent?.GetRichTextEditor();
        if (editor != null)
        {
            var text = editor.Text;
            var selA = editor.SelectionStart;
            var selB = editor.SelectionEnd;
            var selStart = Math.Min(selA, selB);
            var selEnd = Math.Max(selA, selB);
            var selectionLength = selEnd - selStart;
            if (selectionLength > 0)
                text = text.Remove(selStart, selectionLength);
            var caretIndex = Math.Clamp(selectionLength > 0 ? selStart : editor.CaretIndex, 0, text.Length);

            if (_viewModel.OwnerTwoColumn != null
                && QuoteEnterBehavior.TryGetSplitOnEmptyLineEnter(text, caretIndex, out var splitBody, out var splitFollowing))
            {
                _viewModel.NotifyStructuralChangeStarting();
                _viewModel.Content = splitBody;
                _viewModel.RequestExitSplitBelow(splitFollowing);
                return;
            }

            if (_viewModel.Type == BlockType.Quote
                && QuoteEnterBehavior.TryGetSplitOnEmptyLineEnter(text, caretIndex, out var quoteBody, out var followingText))
            {
                _viewModel.NotifyStructuralChangeStarting();
                _viewModel.Content = quoteBody;
                _viewModel.RequestNewBlock(followingText);
                return;
            }

            var isNonEmpty = !BlockEditorContentPolicy.IsVisuallyEmpty(text);
            var isLogicalStart = caretIndex == 0
                || (caretIndex == 1
                    && text.Length > 0
                    && text[0] == BlockEditorContentPolicy.LegacyParagraphSentinel);
            if (selectionLength == 0 && isLogicalStart && isNonEmpty)
            {
                _viewModel.NotifyStructuralChangeStarting();
                _viewModel.PendingCaretIndex = caretIndex;
                _viewModel.RequestNewBlockAbove();
                return;
            }

            var textBefore = text.Substring(0, caretIndex);
            var textAfter = text.Substring(caretIndex);
            _viewModel.NotifyStructuralChangeStarting();
            _viewModel.Content = textBefore;

            if (_viewModel.Type is BlockType.BulletList or BlockType.NumberedList or BlockType.Checklist)
                _viewModel.RequestNewBlockOfType(_viewModel.Type, textAfter);
            else
                _viewModel.RequestNewBlock(textAfter);
        }
        else
        {
            _viewModel.NotifyStructuralChangeStarting();
            _viewModel.RequestNewBlock();
        }
    }
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
        _gutterMarginTopCache = double.NaN;

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
                    rte.Spans = _viewModel.Spans;
            }

            component.TextBoxGotFocus += HandleBlockComponentGotFocus;
            component.TextBoxLostFocus += HandleBlockComponentLostFocus;
            component.TextBoxTextChanged += HandleBlockComponentTextChanged;
            component.TextBoxKeyDown += HandleBlockComponentKeyDown;
            
            var editor = component.GetRichTextEditor();
            if (editor != null)
            {
                _currentEditor = editor;
                editor.IsReadOnly = IsReadOnly;
                editor.ExternalLinkNavigationRequested = OnExternalLinkNavigationRequestedAsync;
                _backspaceTunnelHandler = OnBackspaceTunnelKeyDown;
                editor.AddHandler(InputElement.KeyDownEvent, _backspaceTunnelHandler, Avalonia.Interactivity.RoutingStrategies.Tunnel);

                editor.PointerReleased += OnTextBoxPointerReleasedForToolbar;
                editor.InlineEquationEdited += OnInlineEquationEdited;
                AttachSelectionChangeHandlers(editor);
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

        ApplyReadOnlyState();
        UpdateGutterVerticalAlignment();
    }

    private void ApplyReadOnlyState()
    {
        var readOnly = IsReadOnly;
        if (AddBlockBelowBorder != null)
            AddBlockBelowBorder.IsVisible = !readOnly && !_isNestedInColumn;
        if (DragHandleBorder != null)
            DragHandleBorder.IsVisible = !readOnly && !_isNestedInColumn;
        if (BlockContainer != null)
            DragDrop.SetAllowDrop(BlockContainer, !readOnly);
        if (_currentEditor != null)
            _currentEditor.IsReadOnly = readOnly;
    }

    /// <summary>
    /// Offsets + / drag gutter from the top of the row so it lines up with the first line of real content
    /// (not the padded block chrome), and vertically centers gutter chrome on that line.
    /// </summary>
    private void UpdateGutterVerticalAlignment()
    {
        if (AddBlockBelowBorder == null || DragHandleBorder == null || BlockLayoutGrid == null)
            return;

        double marginTop = 0;
        var grid = BlockLayoutGrid;

        if (_currentBlockComponent is ImageBlockComponent { GutterAnchorRow: { } imageRow })
        {
            var p = imageRow.TranslatePoint(new Point(0, 0), grid);
            if (p.HasValue && imageRow.Bounds.Height > 0)
                marginTop = Math.Max(0, p.Value.Y + (imageRow.Bounds.Height - GutterChromeHeight) * 0.5);
        }
        else if (_currentBlockComponent?.GetRichTextEditor() is RichTextEditor rte)
        {
            var line = rte.GetFirstLineBounds();
            var p = rte.TranslatePoint(new Point(0, line.Y), grid);
            if (p.HasValue && line.Height > 0)
                marginTop = Math.Max(0, p.Value.Y + (line.Height - GutterChromeHeight) * 0.5);
        }
        else if (_currentBlockComponent?.GetLegacyTextBox() is TextBox tb)
        {
            var p = tb.TranslatePoint(new Point(0, 0), grid);
            if (p.HasValue)
            {
                var lineH = tb.FontSize * 1.25;
                marginTop = Math.Max(0, p.Value.Y + (lineH - GutterChromeHeight) * 0.5);
            }
        }
        else if (_currentBlockComponent != null)
        {
            var c = _currentBlockComponent;
            var p = c.TranslatePoint(new Point(0, 0), grid);
            if (p.HasValue && c.Bounds.Height > 0)
                marginTop = Math.Max(0, p.Value.Y + (c.Bounds.Height - GutterChromeHeight) * 0.5);
        }

        if (!double.IsNaN(_gutterMarginTopCache) && Math.Abs(marginTop - _gutterMarginTopCache) < 0.25)
            return;
        _gutterMarginTopCache = marginTop;

        var m = new Thickness(0, marginTop, 2, 0);
        AddBlockBelowBorder.Margin = m;
        DragHandleBorder.Margin = m;
    }

    private void RemoveBackspaceTunnelHandler()
    {
        DetachSelectionChangeHandlers();
        if (_currentEditor != null)
        {
            _currentEditor.ExternalLinkNavigationRequested = null;
            if (_backspaceTunnelHandler != null)
                _currentEditor.RemoveHandler(InputElement.KeyDownEvent, _backspaceTunnelHandler);
            _currentEditor.PointerReleased -= OnTextBoxPointerReleasedForToolbar;
            _currentEditor = null;
            _backspaceTunnelHandler = null;
        }
    }

    private void AttachSelectionChangeHandlers(RichTextEditor editor)
    {
        DetachSelectionChangeHandlers();
        _selectionStartSubscription = editor.GetObservable(RichTextEditor.SelectionStartProperty)
            .Subscribe(new SelectionChangedObserver(OnEditorSelectionChanged));
        _selectionEndSubscription = editor.GetObservable(RichTextEditor.SelectionEndProperty)
            .Subscribe(new SelectionChangedObserver(OnEditorSelectionChanged));
    }

    private void DetachSelectionChangeHandlers()
    {
        _selectionStartSubscription?.Dispose();
        _selectionStartSubscription = null;
        _selectionEndSubscription?.Dispose();
        _selectionEndSubscription = null;
    }

    private void OnEditorSelectionChanged()
    {
        Dispatcher.UIThread.Post(CheckSelectionAndToggleToolbar, DispatcherPriority.Input);
    }

    private sealed class SelectionChangedObserver : IObserver<int>
    {
        private readonly Action _onNext;

        public SelectionChangedObserver(Action onNext)
        {
            _onNext = onNext;
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void OnNext(int value) => _onNext();
    }
    
    private void OnBackspaceTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Back || e.Handled || _viewModel == null || sender is not RichTextEditor textBox)
            return;
        var text = textBox.Text ?? string.Empty;
        var caretIndex = textBox.CaretIndex;
        var selectionLength = Math.Abs(textBox.SelectionEnd - textBox.SelectionStart);

        if (_viewModel.Type == BlockType.Image)
        {
            if (caretIndex != 0 || selectionLength != 0) return;
            if (string.IsNullOrWhiteSpace(text))
            {
                _viewModel.NotifyStructuralChangeStarting();
                _backspaceHandledInTunnel = true;
                e.Handled = true;
                _viewModel.RequestDeleteAndFocusAbove();
            }
            return;
        }

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
        if (_viewModel?.Type == BlockType.Image)
            UpdateEditableBlockAlignment();
    }

    /// <summary>
    /// For image blocks, apply horizontal alignment to the entire EditableBlock (not inner content)
    /// so the selection chrome hugs the image while the block itself moves left/center/right.
    /// Divider uses a full-width line: stretch the content column so the line spans the * grid cell.
    /// </summary>
    private void UpdateEditableBlockAlignment()
    {
        var isDivider = _viewModel?.Type == BlockType.Divider;
        if (BlockContentChrome != null)
            BlockContentChrome.HorizontalAlignment = isDivider ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        if (BlockContentControl != null)
        {
            BlockContentControl.HorizontalAlignment = isDivider ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            BlockContentControl.HorizontalContentAlignment =
                isDivider ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        }

        if (_viewModel?.Type == BlockType.Image)
        {
            var align = ParseImageAlign(_viewModel);
            this.HorizontalAlignment = align;
        }
        else
        {
            this.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private static HorizontalAlignment ParseImageAlign(BlockViewModel vm)
    {
        return vm.ImageAlign.Trim().ToLowerInvariant() switch
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

                    if (_viewModel.PendingCaretPixelX.HasValue)
                    {
                        var px = _viewModel.PendingCaretPixelX.Value;
                        var lastLine = _viewModel.PendingCaretPlaceOnLastLine;
                        _viewModel.PendingCaretPixelX = null;
                        _viewModel.PendingCaretPlaceOnLastLine = false;
                        _focusManager?.FocusTextBoxAtHorizontalOffset(px, lastLine);
                    }
                    else if (_viewModel.PendingCaretIndex.HasValue)
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
                    if (ShouldSuppressCompleteLostFocusForInEditorFocusRedirect()) return;
                    CompleteLostFocus();
                }, DispatcherPriority.Input);
                return;
            }
            if (!ShouldSuppressCompleteLostFocusForInEditorFocusRedirect())
                CompleteLostFocus();
        }
    }

    /// <summary>
    /// Keyboard focus can move to <see cref="BlockEditor"/> (or other chrome under it) for one frame during
    /// a pointer press before the tunnel handler re-focuses the RTE. Clearing <see cref="BlockViewModel.IsFocused"/>
    /// there makes the tunnel think the block was unfocused. Still clear when focus moves to another block.
    /// </summary>
    private bool ShouldSuppressCompleteLostFocusForInEditorFocusRedirect()
    {
        var blockEditor = FindParentBlockEditor();
        var top = TopLevel.GetTopLevel(this);
        var newFocus = top?.FocusManager?.GetFocusedElement() as Visual;
        if (newFocus == null || blockEditor == null)
            return false;
        if (!blockEditor.IsVisualAncestorOf(newFocus))
            return false;

        var newFocusEditable = newFocus.FindAncestorOfType<EditableBlock>();
        if (newFocusEditable != null && !ReferenceEquals(newFocusEditable, this))
            return false;

        return true;
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
        if (IsReadOnly)
            return;
        if (sender is not RichTextEditor editor || _viewModel == null || _stateManager == null)
            return;
        
        if (_stateManager.IsUpdatingFromViewModel)
        {
            return;
        }
        
        var text = editor.Text;
        var previousText = _stateManager.PreviousText;
        
        HandleSlashMenuToggle(text, editor);
        
        if (text == previousText)
            return;

        // RichTextEditor: component already committed via CommitSpansFromEditor; just keep state in sync
        if (sender is RichTextEditor)
        {
            _stateManager.PreviousText = text;
            return;
        }

        _viewModel.PreviousContent = previousText;
        var parentEditor = FindParentBlockEditor();
        parentEditor?.TrackTypingEdit(_viewModel, previousText);

        _viewModel.Content = text;
        _stateManager.PreviousText = text;
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (IsReadOnly)
            return;
        if (_viewModel == null || sender is not RichTextEditor editor || _keyboardHandler == null)
            return;

        if (e.Key == Key.Escape && _stickySubSup != null)
        {
            ClearStickySubSup(editor);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back && _backspaceHandledInTunnel)
        {
            _backspaceHandledInTunnel = false;
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Back)
            _backspaceHandledInTunnel = false;

        if (TryHandleInlineFormatShortcut(e, editor))
            return;

        // Navigation keys clear sticky sub/sup mode; the right-arrow case at a sub/sup span boundary
        // additionally forces the next insertion to be non-sub/sup (escape from trailing style).
        if (IsNavigationKey(e.Key))
        {
            bool isRightArrow = e.Key == Key.Right;
            bool atSubSupBoundary = isRightArrow && IsCaretAtTrailingSubSupBoundary(editor);
            if (_stickySubSup != null)
                ClearStickySubSup(editor);
            if (atSubSupBoundary)
                editor.SetPendingSubSup(false, false); // escape one char, then natural
        }

        if (_viewModel.Type != BlockType.Image)
        {
            // Run after TextInput so "* " / "- " is in the buffer (KeyDown runs before the space is inserted).
            if (e.Key == Key.Space)
            {
                var typeForShortcut = _viewModel.Type;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_viewModel == null || _viewModel.Type != typeForShortcut) return;
                    var ed = _currentBlockComponent?.GetRichTextEditor();
                    if (ed == null) return;
                    _markdownDetector?.TryDetectShortcut(ed, _viewModel.Type);
                }, DispatcherPriority.Input);
            }

            var isSlashKey = e.Key == Key.Divide || e.Key == Key.OemQuestion || e.Key == Key.Oem2;
            if (isSlashKey && BlockEditorContentPolicy.IsVisuallyEmpty(editor.Text) && _stateManager != null && _slashMenuOverlayId == null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var currentText = editor.Text;
                    var stripped = BlockEditorContentPolicy.WithoutLegacySentinel(currentText);
                    if (stripped == "/" && _slashMenuOverlayId == null)
                    {
                        ShowSlashMenu(editor, stripped);
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

            // Slash menu visibility is managed by TextChanged via HandleSlashMenuToggle.
            // Keeping close/open logic in one place avoids key-order race conditions.
        }

        _keyboardHandler.HandleKeyDown(e, editor, _viewModel);
    }

    #endregion

    #region Keyboard and Input Handling

    /// <summary>
    /// Ctrl+B/I/U, Ctrl+Shift+S (strikethrough), Ctrl+Shift+H (highlight), Ctrl+Shift+L (link / unlink),
    /// Ctrl+, (subscript), Ctrl+. (superscript).
    /// Sub/sup use sticky toggle mode when there is no selection: press once to enter, once more to exit.
    /// All other formats apply to the word at caret when there is no selection.
    /// </summary>
    private bool TryHandleInlineFormatShortcut(KeyEventArgs e, RichTextEditor editor)
    {
        if (IsReadOnly || e.Handled || _viewModel == null || _viewModel.Type == BlockType.Code)
            return false;

        var mods = e.KeyModifiers;
        if ((mods & KeyModifiers.Control) == 0 || (mods & KeyModifiers.Alt) != 0)
            return false;

        bool shift = (mods & KeyModifiers.Shift) != 0;

        if (e.Key == Key.L && shift)
        {
            e.Handled = true;
            _ = HandleLinkShortcutAsync(editor);
            return true;
        }

        if (e.Key == Key.E && shift)
        {
            ApplyInlineEquation();
            e.Handled = true;
            return true;
        }

        // Sub/superscript use sticky toggle mode (no shift, no Alt already gated above).
        if (e.Key == Key.OemComma || e.Key == Key.OemPeriod)
        {
            var subSupKind = e.Key == Key.OemComma ? InlineFormatKind.Subscript : InlineFormatKind.Superscript;
            HandleSubSupShortcut(editor, subSupKind);
            e.Handled = true;
            return true;
        }

        InlineFormatKind? kind = e.Key switch
        {
            Key.B when !shift => InlineFormatKind.Bold,
            Key.I when !shift => InlineFormatKind.Italic,
            Key.U when !shift => InlineFormatKind.Underline,
            Key.S when shift => InlineFormatKind.Strikethrough,
            Key.H when shift => InlineFormatKind.Highlight,
            _ => null
        };
        if (kind == null)
            return false;

        if (kind == InlineFormatKind.Bold
            && _viewModel.Type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3 or BlockType.Heading4)
            return false;

        string? color = null;
        if (kind == InlineFormatKind.Highlight
            && Application.Current?.TryFindResource("InlineHighlightColor", out var res) == true
            && res is Color c)
            color = c.ToString();

        var blockEditor = FindParentBlockEditor();
        if (blockEditor?.HasCrossBlockTextSelection() == true)
        {
            ApplyInlineFormat(kind.Value, color);
            e.Handled = true;
            return true;
        }

        if (GetSelectionRange() == null)
        {
            var word = editor.TryGetWordRangeAtCaret();
            if (word == null)
                return false;
            editor.SelectionStart = word.Value.Start;
            editor.SelectionEnd = word.Value.End;
            editor.CaretIndex = word.Value.End;
        }

        ApplyInlineFormat(kind.Value, color);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// Handles Ctrl+, (subscript) and Ctrl+. (superscript).
    /// With a selection: toggles the format on the selected range.
    /// Without a selection: enters/exits sticky typing mode. Pressing the same shortcut again exits.
    /// Pressing the opposite shortcut switches directly to the other mode.
    /// </summary>
    private void HandleSubSupShortcut(RichTextEditor editor, InlineFormatKind kind)
    {
        var blockEditor = FindParentBlockEditor();
        bool hasCrossBlock = blockEditor?.HasCrossBlockTextSelection() == true;
        bool hasSelection = hasCrossBlock || GetSelectionRange() != null;

        if (hasSelection)
        {
            // Clear sticky mode — the explicit selection takes precedence.
            SetStickySubSup(editor, null);
            ApplyInlineFormat(kind, null);
            return;
        }

        // Sticky toggle: same kind → turn off, different kind → switch, none → turn on.
        var newKind = _stickySubSup == kind ? (InlineFormatKind?)null : kind;
        SetStickySubSup(editor, newKind);
    }

    /// <summary>
    /// Sets or clears sticky sub/sup typing mode and updates the editor's pending insert style.
    /// When clearing (kind == null) and the caret is inside a sub/sup span, activates escape mode
    /// so the next character is forced to normal style instead of inheriting the adjacent span.
    /// </summary>
    private void SetStickySubSup(RichTextEditor editor, InlineFormatKind? kind)
    {
        _stickySubSup = kind;
        if (kind == InlineFormatKind.Subscript)
            editor.SetPendingSubSup(true, false);
        else if (kind == InlineFormatKind.Superscript)
            editor.SetPendingSubSup(false, true);
        else if (IsCaretAdjacentToSubSupSpan(editor))
            editor.SetPendingSubSup(false, false); // escape mode: force next char to normal
        else
            editor.SetPendingSubSup(null, null);
        UpdateFormattingToolbarState();
    }

    /// <summary>
    /// Clears sticky sub/sup mode and removes the pending insert style override.
    /// Should be called on navigation keys (arrow/home/end) and pointer clicks.
    /// </summary>
    private void ClearStickySubSup(RichTextEditor editor)
    {
        if (_stickySubSup == null) return;
        SetStickySubSup(editor, null);
    }

    /// <summary>
    /// Returns true when either the character immediately to the left <em>or</em> the character
    /// immediately to the right of the caret belongs to a subscript or superscript span.
    /// Checking both sides ensures that pressing a navigation key to exit sticky mode always sets
    /// escape-mode, even when the caret is at the leading edge of a sub/sup span (left is normal,
    /// right is sub/sup) and the key will move into that span.
    /// </summary>
    private bool IsCaretAdjacentToSubSupSpan(RichTextEditor editor)
    {
        var spans = _viewModel?.Spans;
        if (spans == null) return false;
        int caret = editor.CaretIndex;

        int pos = 0;
        foreach (var span in spans)
        {
            int len = span is Core.Models.TextSpan ts ? ts.Text.Length : 1;
            int end = pos + len;

            if (span is Core.Models.TextSpan textSpan &&
                (textSpan.Style.Subscript || textSpan.Style.Superscript))
            {
                // Left neighbor: span contains the char just before the caret.
                if (caret > 0 && pos < caret && end >= caret)
                    return true;
                // Right neighbor: span starts right at the caret (char immediately to the right).
                if (pos == caret && len > 0)
                    return true;
            }

            pos = end;
        }
        return false;
    }

    private static bool IsNavigationKey(Key key) =>
        key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End;

    /// <summary>
    /// Returns true when the caret is at the position immediately after a sub/sup-styled span,
    /// and the span (or next position) is NOT already sub/sup — i.e. the boundary where typing
    /// would undesirably inherit sub/sup style from the left span.
    /// </summary>
    private bool IsCaretAtTrailingSubSupBoundary(RichTextEditor editor)
    {
        if (_viewModel == null) return false;
        var spans = _viewModel.Spans;
        int caret = editor.CaretIndex;
        if (caret <= 0) return false;

        int pos = 0;
        Core.Models.TextStyle? leftStyle = null;
        Core.Models.TextStyle? rightStyle = null;
        foreach (var span in spans)
        {
            int len = span is Core.Models.TextSpan ts ? ts.Text.Length : 1;
            int runEnd = pos + len;
            if (runEnd == caret && span is Core.Models.TextSpan lt)
                leftStyle = lt.Style;
            if (pos == caret && span is Core.Models.TextSpan rt)
                rightStyle = rt.Style;
            pos = runEnd;
        }

        if (leftStyle == null) return false;
        bool leftIsSubSup = leftStyle.Value.Subscript || leftStyle.Value.Superscript;
        bool rightIsSubSup = rightStyle?.Subscript == true || rightStyle?.Superscript == true;
        return leftIsSubSup && !rightIsSubSup;
    }

    private static (int start, int end)? TryGetContiguousLinkSpanAtCaret(RichTextEditor editor, IReadOnlyList<InlineSpan> runs)
    {
        var flatLen = InlineSpanFormatApplier.Flatten(runs).Length;
        if (flatLen == 0) return null;
        int caret = Math.Clamp(editor.CaretIndex, 0, flatLen);
        int idx = caret >= flatLen ? flatLen - 1 : caret;
        string? url = GetLinkUrlAtCharIndex(runs, idx);
        if (string.IsNullOrEmpty(url)) return null;
        int s = idx;
        while (s > 0 && string.Equals(GetLinkUrlAtCharIndex(runs, s - 1), url, StringComparison.OrdinalIgnoreCase))
            s--;
        int end = idx + 1;
        while (end < flatLen && string.Equals(GetLinkUrlAtCharIndex(runs, end), url, StringComparison.OrdinalIgnoreCase))
            end++;
        return (s, end);
    }

    private static string? GetLinkUrlAtCharIndex(IReadOnlyList<InlineSpan> runs, int index)
    {
        int pos = 0;
        foreach (var seg in runs)
        {
            int len = seg is TextSpan t ? t.Text.Length : 1;
            int end = pos + len;
            if (index < end && index >= pos)
                return seg is TextSpan tx ? tx.Style.LinkUrl : null;
            pos = end;
        }

        return null;
    }

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

        var filterSource = BlockEditorContentPolicy.WithoutLegacySentinel(text);
        var isSlashCommand = filterSource.StartsWith("/");
        var menuIsVisible = _slashMenuOverlayId != null;

        // Always ensure state matches reality - correct any mismatches
        if (isSlashCommand && !menuIsVisible)
        {
            ShowSlashMenu(textBox, filterSource);
            _stateManager.SetShowingSlashMenu();
        }
        else if (isSlashCommand && menuIsVisible && _currentSlashMenu != null)
        {
            // Update filter if menu is already visible
            _currentSlashMenu.UpdateFilter(filterSource);
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
            if (meta != null
                && meta.TryGetValue(MarkdownShortcutDetector.ShortcutReplacementContentKey, out var repl)
                && repl is string replacementText)
            {
                _viewModel.Content = replacementText;
            }
            else
                _viewModel.Content = string.Empty;
            _stateManager.PreviousText = _viewModel.Content ?? string.Empty;
            _focusManager?.ClearCache();
            
            if (meta != null)
            {
                if (blockType == BlockType.Code && meta.TryGetValue("language", out var langObj) && langObj != null)
                    _viewModel.CodeLanguage = langObj.ToString() ?? "csharp";
                foreach (var kvp in meta)
                {
                    if (kvp.Key == MarkdownShortcutDetector.ShortcutReplacementContentKey)
                        continue;
                    if (blockType == BlockType.Code && kvp.Key == "language")
                        continue;
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

    private static bool IsEditableBlockType(BlockType type) => type is not BlockType.Divider and not BlockType.Image and not BlockType.Equation;

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

        if (blockType == BlockType.TwoColumn)
        {
            if (_viewModel.OwnerTwoColumn != null)
            {
                _stateManager.SetNormal();
                _focusManager?.ClearCache();
                return;
            }

            var editor = FindParentBlockEditor();
            if (editor != null)
            {
                _stateManager.PreviousText = string.Empty;
                editor.ReplaceBlockWithTwoColumn(_viewModel);
                _stateManager.SetNormal();
                _focusManager?.ClearCache();
                return;
            }
        }

        _viewModel.NotifyStructuralChangeStarting();
        _viewModel.Type = blockType;
        _viewModel.Content = string.Empty;
        _stateManager.PreviousText = string.Empty;
        _stateManager.SetNormal();
        _focusManager?.ClearCache();

        // When converting to a non-editable block, ensure there is an editable block below so the user can keep typing
        var addedBlockBelow = blockType is BlockType.Divider or BlockType.Image or BlockType.Equation && EnsureEditableBlockBelowIfNeeded();

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
        if (_stickySubSup != null && sender is RichTextEditor editor)
            ClearStickySubSup(editor);
    }

    private void CheckSelectionAndToggleToolbar()
    {
        var parentEditor = FindParentBlockEditor();
        if (parentEditor?.HasCrossBlockTextSelection() == true && _viewModel?.IsFocused != true)
        {
            _cachedSelectionRange = null;
            CloseFormattingToolbar();
            return;
        }

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
        toolbar.EquationRequested += OnEquationRequested;
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
                _currentFormattingToolbar.EquationRequested -= OnEquationRequested;
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
        var state = GetFormatStateForRange(_viewModel.Spans, start, end);

        // Sticky sub/sup overrides the span-derived state when no selection is active.
        bool subActive = _stickySubSup == InlineFormatKind.Subscript || state.subscript;
        bool supActive = _stickySubSup == InlineFormatKind.Superscript || state.superscript;

        _currentFormattingToolbar.UpdateFormatState(
            state.bold, state.italic, state.underline, state.strikethrough,
            state.highlight, state.backgroundColor, state.hasLink,
            subActive, supActive);
        var heading = _viewModel.Type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3 or BlockType.Heading4;
        _currentFormattingToolbar.SetBoldButtonEnabled(!heading);
    }

    private static (bool bold, bool italic, bool underline, bool strikethrough, bool highlight, string? backgroundColor, bool hasLink, bool subscript, bool superscript) GetFormatStateForRange(IReadOnlyList<InlineSpan> runs, int start, int end)
    {
        if (runs.Count == 0 || start >= end) return (false, false, false, false, false, null, false, false, false);
        bool bold = true, italic = true, underline = true, strikethrough = true, highlight = true;
        bool subscript = true, superscript = true;
        string? backgroundColor = null;
        bool backgroundMixed = false;
        bool anyOverlap = false;
        bool hasLink = false;
        int pos = 0;
        foreach (var seg in runs)
        {
            if (seg is not TextSpan run)
            {
                pos += 1;
                continue;
            }

            int runEnd = pos + run.Text.Length;
            if (runEnd <= start || pos >= end) { pos = runEnd; continue; }
            anyOverlap = true;
            if (!run.Style.Bold) bold = false;
            if (!run.Style.Italic) italic = false;
            if (!run.Style.Underline) underline = false;
            if (!run.Style.Strikethrough) strikethrough = false;
            if (!run.Style.Highlight) highlight = false;
            if (!run.Style.Subscript) subscript = false;
            if (!run.Style.Superscript) superscript = false;
            if (!backgroundMixed && run.Style.BackgroundColor != null)
            {
                if (backgroundColor == null) backgroundColor = run.Style.BackgroundColor;
                else if (backgroundColor != run.Style.BackgroundColor)
                {
                    backgroundColor = null;
                    backgroundMixed = true;
                }
            }
            if (run.Style.LinkUrl != null) hasLink = true;
            pos = runEnd;
        }
        if (!anyOverlap) return (false, false, false, false, false, null, false, false, false);
        return (bold, italic, underline, strikethrough, highlight, backgroundColor, hasLink, subscript, superscript);
    }

    private void OnFormatRequested(InlineFormatKind kind)
    {
        if (kind == InlineFormatKind.Link)
        {
            _ = HandleLinkFormatRequestedAsync();
            return;
        }

        if (kind is InlineFormatKind.Subscript or InlineFormatKind.Superscript)
        {
            var editor = _currentBlockComponent?.GetRichTextEditor() ?? _focusManager?.GetCurrentTextBox();
            if (editor != null)
                HandleSubSupShortcut(editor, kind);
            return;
        }

        string? color = null;
        if (kind == InlineFormatKind.Highlight && Application.Current?.TryFindResource("InlineHighlightColor", out var res) == true && res is Color c)
            color = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        ApplyInlineFormat(kind, color);
    }

    private void OnEquationRequested() => ApplyInlineEquation();

    private void OnInlineEquationEdited(int charIndex, string newLatex)
    {
        if (_viewModel == null) return;
        var spans = _viewModel.Spans.ToList();
        int offset = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            int len = spans[i] is TextSpan t ? t.Text.Length : 1;
            int runEnd = offset + len;
            if (charIndex >= offset && charIndex < runEnd && spans[i] is EquationSpan eq)
            {
                spans[i] = eq with { Latex = newLatex };
                _viewModel.CommitSpansFromEditor(spans);
                var editor = _currentBlockComponent?.GetRichTextEditor();
                if (editor != null)
                    editor.Spans = _viewModel.Spans;
                return;
            }

            offset = runEnd;
        }
    }

    private void ApplyInlineEquation()
    {
        var editor = _currentBlockComponent?.GetRichTextEditor() ?? _focusManager?.GetCurrentTextBox();
        if (_viewModel == null || editor == null) return;

        var range = GetSelectionRange() ?? _cachedSelectionRange;
        int start, end;
        if (range == null || range.Value.start >= range.Value.end)
        {
            var word = editor.TryGetWordRangeAtCaret();
            if (word == null) return;
            (start, end) = word.Value;
            editor.SelectionStart = start;
            editor.SelectionEnd = end;
            editor.CaretIndex = end;
        }
        else
        {
            (start, end) = range.Value;
        }

        if (start >= end) return;

        var selectedText = Core.Formatting.InlineSpanFormatApplier.Flatten(
            Core.Formatting.InlineSpanFormatApplier.SliceRuns(_viewModel.Spans, start, end));
        var latex = Core.Formatting.EquationLatexNormalizer.Normalize(selectedText);

        _viewModel.ApplyFormat(start, end, InlineFormatKind.Equation, latex);

        editor.Spans = _viewModel.Spans;
        editor.CaretIndex = start + 1;
        editor.SelectionStart = start + 1;
        editor.SelectionEnd = start + 1;

        CloseFormattingToolbar();
    }

    private async Task HandleLinkFormatRequestedAsync(bool expandWordWhenNoSelection = true)
    {
        var blockEditor = FindParentBlockEditor();
        var editor = _currentBlockComponent?.GetRichTextEditor() ?? _focusManager?.GetCurrentTextBox();

        if (editor != null && blockEditor?.HasCrossBlockTextSelection() != true && _viewModel != null)
        {
            if (GetSelectionRange() == null && _cachedSelectionRange == null)
            {
                var linkSpan = TryGetContiguousLinkSpanAtCaret(editor, _viewModel.Spans);
                if (linkSpan != null)
                {
                    editor.SelectionStart = linkSpan.Value.start;
                    editor.SelectionEnd = linkSpan.Value.end;
                    editor.CaretIndex = linkSpan.Value.end;
                    _cachedSelectionRange = (linkSpan.Value.start, linkSpan.Value.end);
                }
                else if (expandWordWhenNoSelection)
                {
                    var word = editor.TryGetWordRangeAtCaret();
                    if (word != null)
                    {
                        editor.SelectionStart = word.Value.Start;
                        editor.SelectionEnd = word.Value.End;
                        editor.CaretIndex = word.Value.End;
                        _cachedSelectionRange = (word.Value.Start, word.Value.End);
                    }
                }
            }
        }

        if (blockEditor?.HasCrossBlockTextSelection() == true)
        {
            bool hasLink = blockEditor.CrossBlockTextSelectionHasLink();
            string? crossUrl = hasLink ? blockEditor.TryGetFirstLinkUrlInCrossBlockSelection() : null;
            var result = await ShowLinkEditDialogAsync(
                initialUrl: crossUrl ?? string.Empty,
                initialDisplay: string.Empty,
                showDisplaySection: false,
                showCrossBlockHint: true,
                showRemoveLink: hasLink,
                wasEditingLink: hasLink,
                titleKey: hasLink ? "EditLinkTitle" : "InsertLinkTitle");
            if (result == null) return;
            if (result.RemoveLinkRequested)
            {
                ApplyInlineFormat(InlineFormatKind.Link, null);
                return;
            }
            if (string.IsNullOrWhiteSpace(result.Url)) return;
            ApplyInlineFormat(InlineFormatKind.Link, NormalizeUrlInput(result.Url.Trim()));
            return;
        }

        var range = GetSelectionRange() ?? _cachedSelectionRange;
        if (range == null || _viewModel == null) return;

        var flat = InlineSpanFormatApplier.Flatten(_viewModel.Spans);
        var (a, b) = range.Value;
        if (a >= b || b > flat.Length) return;
        string selectedText = flat.Substring(a, b - a);
        bool hasLinkSel = GetFormatStateForRange(_viewModel.Spans, a, b).hasLink;
        string? initialUrl = hasLinkSel ? GetLinkUrlForRange(_viewModel.Spans, a, b) : null;

        var dlgResult = await ShowLinkEditDialogAsync(
            initialUrl: initialUrl ?? string.Empty,
            initialDisplay: selectedText,
            showDisplaySection: true,
            showCrossBlockHint: false,
            showRemoveLink: hasLinkSel,
            wasEditingLink: hasLinkSel,
            titleKey: hasLinkSel ? "EditLinkTitle" : "InsertLinkTitle");
        if (dlgResult == null) return;

        if (dlgResult.RemoveLinkRequested)
        {
            ApplyInlineFormat(InlineFormatKind.Link, null);
            return;
        }
        if (string.IsNullOrWhiteSpace(dlgResult.Url))
        {
            if (hasLinkSel)
                ApplyInlineFormat(InlineFormatKind.Link, null);
            return;
        }

        var normalized = NormalizeUrlInput(dlgResult.Url.Trim());
        CommitSingleBlockLinkApply(range.Value, dlgResult.DisplayText, normalized);
    }

    private void CommitSingleBlockLinkApply((int start, int end) range, string displayText, string normalizedUrl)
    {
        var editor = _currentBlockComponent?.GetRichTextEditor() ?? _focusManager?.GetCurrentTextBox();
        if (editor == null || _viewModel == null) return;

        var (newStart, newEnd) = _viewModel.ApplyLinkEdit(range.start, range.end, displayText, normalizedUrl, removeLink: false);

        _stateManager?.SetUpdatingFromViewModel();
        editor.Spans = _viewModel.Spans;
        editor.SelectionStart = newStart;
        editor.SelectionEnd = newEnd;
        editor.CaretIndex = newEnd;
        if (_stateManager != null)
            _stateManager.PreviousText = _viewModel.Content;
        _stateManager?.SetNormal();
        _cachedSelectionRange = (newStart, newEnd);
        UpdateFormattingToolbarState();
        editor.Focus();
    }

    private static string? GetLinkUrlForRange(IReadOnlyList<InlineSpan> runs, int start, int end)
    {
        int pos = 0;
        foreach (var seg in runs)
        {
            if (seg is not TextSpan run)
            {
                pos += 1;
                continue;
            }

            int re = pos + run.Text.Length;
            if (re > start && pos < end && run.Style.LinkUrl != null)
                return run.Style.LinkUrl;
            pos = re;
        }
        return null;
    }

    private Task<LinkEditDialogResult?> ShowLinkEditDialogAsync(
        string initialUrl,
        string initialDisplay,
        bool showDisplaySection,
        bool showCrossBlockHint,
        bool showRemoveLink,
        bool wasEditingLink,
        string titleKey)
    {
        var tcs = new TaskCompletionSource<LinkEditDialogResult?>();
        var overlaySvc = _overlayService ?? (Application.Current as App)?.Services?.GetService<IOverlayService>();
        if (overlaySvc == null)
        {
            tcs.SetResult(null);
            return tcs.Task;
        }

        var loc = (Application.Current as App)?.Services?.GetService<ILocalizationService>();
        string T(string key, string ns = "NotesEditor") => loc?.T(key, ns) ?? key;

        var dialog = new LinkInsertDialogOverlay
        {
            Title = T(titleKey),
            UrlLabel = T("InsertLinkUrlLabel"),
            Url = initialUrl,
            UrlPlaceholder = T("InsertLinkUrlPlaceholder"),
            DisplayLabel = T("InsertLinkDisplayLabel"),
            DisplayText = initialDisplay,
            DisplayPlaceholder = T("InsertLinkDisplayPlaceholder"),
            ShowDisplaySection = showDisplaySection,
            ShowCrossBlockHint = showCrossBlockHint,
            CrossBlockHint = showCrossBlockHint ? T("CrossBlockLinkHint") : null,
            ShowRemoveLink = showRemoveLink,
            RemoveLinkText = T("InsertLinkRemoveLink"),
            ConfirmText = loc?.T("OK", "Common") ?? "OK",
            CancelText = loc?.T("Cancel", "Common") ?? "Cancel",
            RequireUrlForConfirm = !wasEditingLink
        };

        var id = overlaySvc.CreateOverlay(dialog, new OverlayOptions
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ShowBackdrop = true,
            CloseOnOutsideClick = true,
            CloseOnEscape = true
        }, "LinkInsert");

        dialog.OnResult = result =>
        {
            overlaySvc.CloseOverlay(id);
            tcs.TrySetResult(result);
        };

        return tcs.Task;
    }

    /// <summary>Ctrl+Shift+L: same dialog as toolbar; only expands selection to a contiguous link span (no word expansion).</summary>
    private async Task HandleLinkShortcutAsync(RichTextEditor editor)
    {
        var blockEditor = FindParentBlockEditor();
        if (blockEditor == null || _viewModel == null) return;

        if (blockEditor.HasCrossBlockTextSelection())
        {
            await HandleLinkFormatRequestedAsync(expandWordWhenNoSelection: false);
            return;
        }

        if (GetSelectionRange() == null)
        {
            var span = TryGetContiguousLinkSpanAtCaret(editor, _viewModel.Spans);
            if (span == null)
                return;
            editor.SelectionStart = span.Value.start;
            editor.SelectionEnd = span.Value.end;
            editor.CaretIndex = span.Value.end;
            _cachedSelectionRange = (span.Value.start, span.Value.end);
        }

        await HandleLinkFormatRequestedAsync(expandWordWhenNoSelection: false);
    }

    private static string NormalizeUrlInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        if (Uri.TryCreate(input, UriKind.Absolute, out var abs))
            return abs.ToString();
        if (input.Contains('@', StringComparison.Ordinal) && !input.Contains("://", StringComparison.Ordinal))
            return "mailto:" + input;
        if (!input.Contains("://", StringComparison.Ordinal))
            return "https://" + input;
        return input;
    }

    private async Task OnExternalLinkNavigationRequestedAsync(string url)
    {
        if (await ConfirmExternalNavigationAsync(url))
            RichTextEditor.OpenExternalUrl(url, this);
    }

    private async Task<bool> ConfirmExternalNavigationAsync(string url)
    {
        var overlaySvc = _overlayService ?? (Application.Current as App)?.Services?.GetService<IOverlayService>();
        if (overlaySvc == null)
            return true;

        var loc = (Application.Current as App)?.Services?.GetService<ILocalizationService>();
        string T(string key, string ns = "NotesEditor") => loc?.T(key, ns) ?? key;
        var continueLabel = loc?.T("Continue", "Common") ?? "Continue";
        var cancelLabel = loc?.T("Cancel", "Common") ?? "Cancel";

        var tcs = new TaskCompletionSource<bool>();
        var dialog = new DialogOverlay
        {
            Title = T("ExternalLinkTitle"),
            Description = string.Format(T("ExternalLinkMessage"), url),
            PrimaryText = continueLabel,
            SecondaryText = cancelLabel
        };

        var id = overlaySvc.CreateOverlay(dialog, new OverlayOptions
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ShowBackdrop = true,
            CloseOnOutsideClick = false,
            CloseOnEscape = true
        }, "ExternalLinkConfirm");

        dialog.OnChoose = choice =>
        {
            overlaySvc.CloseOverlay(id);
            tcs.TrySetResult(string.Equals(choice, continueLabel, StringComparison.Ordinal));
        };

        return await tcs.Task;
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

        if (kind == InlineFormatKind.Bold
            && _viewModel.Type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3 or BlockType.Heading4)
            return;

        var range = GetSelectionRange() ?? _cachedSelectionRange;
        if (range == null) return;

        string previousText = _viewModel.Content;

        var (newSelStart, newSelEnd) = _viewModel.ApplyFormat(range.Value.start, range.Value.end, kind, color);

        _stateManager?.SetUpdatingFromViewModel();

        // Sync runs from VM into editor
        editor.Spans = _viewModel.Spans;
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

    private void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_blockGutterChromeVisible || _viewModel == null || DragHandleBorder == null) return;
        if (!e.GetCurrentPoint(DragHandleBorder).Properties.IsLeftButtonPressed) return;

        _dragHandleReorderDragLaunched = false;
        _dragHandleReorderPressPoint = e.GetPosition(DragHandleBorder);
        e.Pointer.Capture(DragHandleBorder);
        e.Handled = true;
    }

    private void DragHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (DragHandleBorder == null || !_dragHandleReorderPressPoint.HasValue || _dragHandleReorderDragLaunched) return;
        if (!ReferenceEquals(e.Pointer.Captured, DragHandleBorder)) return;
        if (!e.GetCurrentPoint(DragHandleBorder).Properties.IsLeftButtonPressed) return;

        var p = e.GetPosition(DragHandleBorder);
        var origin = _dragHandleReorderPressPoint.Value;
        var dx = p.X - origin.X;
        var dy = p.Y - origin.Y;
        if (Math.Sqrt(dx * dx + dy * dy) >= DragHandleReorderDragThresholdPixels)
        {
            _dragHandleReorderDragLaunched = true;
            _ = RunDragReorderFromHandleAsync(e);
        }
    }

    private void DragHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DragHandleBorder == null) return;
        if (e.GetCurrentPoint(DragHandleBorder).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;

        if (!_dragHandleReorderDragLaunched && _dragHandleReorderPressPoint.HasValue && _viewModel != null)
            FindParentBlockEditor()?.SelectBlockFromDragHandle(_viewModel, e.KeyModifiers);

        if (ReferenceEquals(e.Pointer.Captured, DragHandleBorder))
            e.Pointer.Capture(null);

        ClearDragHandleReorderGestureState();
    }

    private void DragHandle_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Releasing capture to start DoDragDrop fires this — do not clear; RunDragReorderFromHandleAsync clears after drop.
        if (_dragHandleReorderDragLaunched)
            return;
        ClearDragHandleReorderGestureState();
    }

    private void ClearDragHandleReorderGestureState()
    {
        _dragHandleReorderPressPoint = null;
        _dragHandleReorderDragLaunched = false;
    }

    private async Task RunDragReorderFromHandleAsync(PointerEventArgs e)
    {
        try
        {
            if (DragHandleBorder != null && ReferenceEquals(e.Pointer.Captured, DragHandleBorder))
                e.Pointer.Capture(null);

            await BeginBlockReorderDragCoreAsync(e).ConfigureAwait(true);
        }
        finally
        {
            ClearDragHandleReorderGestureState();
        }
    }

    /// <summary>Gutter handle or image chrome (after move threshold) — same payload and ghost as the drag handle.</summary>
    internal async Task BeginBlockReorderDragCoreAsync(PointerEventArgs e)
    {
        if (_viewModel == null) return;

        var editor = FindParentBlockEditor();
        if (editor == null) return;

        var payload = editor.CreateBlockReorderPayload(_viewModel, e.KeyModifiers);
        editor.ClearBlockSelection();

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(BlockViewModel.BlockDragDataFormat, payload.Primary));
        transfer.Add(DataTransferItem.Create(BlockViewModel.BlockReorderDragPayload.Format, payload));

        editor.BeginBlockDragGhost(this, e);
        try
        {
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move).ConfigureAwait(true);
        }
        finally
        {
            editor.EndBlockDragGhost();
        }
    }

    private static bool TryResolveBlockReorderPayload(DragEventArgs e, out BlockViewModel.BlockReorderDragPayload? payload)
    {
        if (e.DataTransfer.TryGetValue(BlockViewModel.BlockReorderDragPayload.Format) is BlockViewModel.BlockReorderDragPayload p)
        {
            payload = p;
            return true;
        }

        if (e.DataTransfer.TryGetValue(BlockViewModel.BlockDragDataFormat) is not { } vm)
        {
            payload = null;
            return false;
        }

        payload = new BlockViewModel.BlockReorderDragPayload
        {
            Primary = vm,
            BlocksInDocumentOrder = new[] { vm }
        };
        return true;
    }

    private void Block_DragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(BlockViewModel.BlockDragDataFormat)
            && !e.DataTransfer.Contains(BlockViewModel.BlockReorderDragPayload.Format))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        if (!TryResolveBlockReorderPayload(e, out var payload) || payload == null)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var parent = FindParentBlockEditor();
        Point cursorInEditor = parent != null ? e.GetPosition(parent) : default;
        if (parent != null)
            parent.UpdateBlockDragGhostFromEditorPoint(cursorInEditor);

        // Self-hover normally skips drop feedback (single block). Multi-block drags still need insert-from-Y.
        if (payload.BlocksInDocumentOrder.Count == 1 && ReferenceEquals(payload.Primary, _viewModel))
        {
            var blocks = parent?.Blocks;
            if (blocks == null || payload.Primary.GetColumnSibling(blocks) == null)
            {
                e.DragEffects = DragDropEffects.Move;
                return;
            }
        }

        e.DragEffects = DragDropEffects.Move;

        if (parent == null) return;

        parent.HandleBlockDragOver(cursorInEditor, payload);
        e.Handled = true;
    }

    public void ShowDropLineAtLeft()
    {
        if (DropIndicatorLineVerticalRight != null) DropIndicatorLineVerticalRight.IsVisible = false;
        if (DropIndicatorLineVerticalLeft != null)
            DropIndicatorLineVerticalLeft.IsVisible = true;
    }

    public void ShowDropLineAtRight()
    {
        if (DropIndicatorLineVerticalLeft != null) DropIndicatorLineVerticalLeft.IsVisible = false;
        if (DropIndicatorLineVerticalRight != null)
            DropIndicatorLineVerticalRight.IsVisible = true;
    }

    public void HideDropLine()
    {
        if (DropIndicatorLineVerticalLeft != null)
            DropIndicatorLineVerticalLeft.IsVisible = false;
        if (DropIndicatorLineVerticalRight != null)
            DropIndicatorLineVerticalRight.IsVisible = false;
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
        if (!TryResolveBlockReorderPayload(e, out var payload) || payload == null) return;

        var parent = FindParentBlockEditor();
        if (parent == null) return;

        try
        {
            parent.TryPerformDrop(payload);
        }
        catch (Exception)
        {
        }
        finally
        {
            parent.ClearDropIndicator();
        }

        e.Handled = true;
    }

    /// <summary>This block's RTE only — never the globally focused editor (cross-block selection must paint on each block).</summary>
    private RichTextEditor? GetEditor() =>
        _currentBlockComponent?.GetRichTextEditor();

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

    /// <summary>True if the current non-empty selection overlaps any linked run.</summary>
    internal bool DoesSelectionHaveLink()
    {
        if (_viewModel == null) return false;
        var range = GetSelectionRange();
        if (range == null) return false;
        return GetFormatStateForRange(_viewModel.Spans, range.Value.start, range.Value.end).hasLink;
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
        int end = range.Value.end;
        int len = end - start;
        if (text.Length == 0 && start == 0 && end == 1)
        {
            editor.SelectionStart = 0;
            editor.SelectionEnd = 0;
            editor.CaretIndex = 0;
            return true;
        }
        if (len <= 0 || start < 0 || start + len > text.Length) return false;
        // Let RichTextEditor handle the run-aware deletion
        editor.SelectionStart = start;
        editor.SelectionEnd = start + len;
        // Trigger deletion by invoking the internal delete logic via key simulation is complex;
        // instead manipulate runs directly through InlineSpanFormatApplier
        var newRuns = Core.Formatting.InlineSpanFormatApplier.ApplyTextEdit(
            editor.Spans, text, text.Remove(start, len));
        editor.Spans = newRuns;
        editor.CaretIndex = start;
        editor.SelectionStart = start;
        editor.SelectionEnd = start;
        _viewModel.CommitSpansFromEditor(editor.Spans);
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
        var newRuns = Core.Formatting.InlineSpanFormatApplier.ApplyTextEdit(editor.Spans, flat, newFlat);
        editor.Spans = newRuns;
        int newCaret = start + text.Length;
        editor.CaretIndex = newCaret;
        editor.SelectionStart = newCaret;
        editor.SelectionEnd = newCaret;
        _viewModel.CommitSpansFromEditor(editor.Spans);
        return true;
    }

    /// <summary>Flat text length for clamping caret/anchor when starting a cross-block gesture (trailing edge = length).</summary>
    public int GetAnchorCharClampMax()
    {
        var rte = TryGetRichTextEditor();
        if (rte != null) return rte.TextLength;
        return _viewModel?.Content?.Length ?? 0;
    }

    /// <summary>
    /// Length used when extending cross-block selection through a block; empty rich blocks count as 1 logical char.
    /// </summary>
    public int GetLogicalTextLengthForCrossBlockSelection()
    {
        var rte = TryGetRichTextEditor();
        if (rte != null) return rte.SelectionIndexUpperBound;
        return _viewModel?.Content?.Length ?? 0;
    }

    public void ApplyTextSelection(int start, int end)
    {
        var editor = GetEditor();
        if (editor == null) return;
        int len = editor is RichTextEditor rte ? rte.SelectionIndexUpperBound : editor.TextLength;
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

