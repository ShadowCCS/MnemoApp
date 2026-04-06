using Avalonia.Input;
using Mnemo.Core.Models;
using System;

namespace Mnemo.UI.Components.BlockEditor;

public class KeyboardHandler
{
    private Key? _lastKey;
    public Key? LastKey { get => _lastKey; set => _lastKey = value; }
    public bool WasBackspace => _lastKey == Key.Back;

    public event Action? BackspaceOnEmpty;
    /// <summary>Nullable horizontal offset in source <see cref="RichTextEditor"/> layout space; null if unknown.</summary>
    public event Action<double?>? RequestFocusPrevious;
    public event Action<double?>? RequestFocusNext;
#pragma warning disable CS0067
    public event Action? RequestNewBlock;
#pragma warning restore CS0067
    public event Action<BlockType>? ConvertToBlockType;
    public event Action? ConvertToTextPreservingContent;
    public event Action? EscapePressed;
    public event Action? EnterPressed;
    public event Action? MergeWithPrevious;

    public void HandleKeyDown(KeyEventArgs e, RichTextEditor editor, BlockViewModel? viewModel)
    {
        if (e.Handled || viewModel == null || editor == null) return;

        LastKey = e.Key;
        var text = editor.Text;
        var caretIndex = editor.CaretIndex;
        var selectionLength = Math.Abs(editor.SelectionEnd - editor.SelectionStart);

        switch (e.Key)
        {
            case Key.Enter when viewModel.Type != BlockType.Code:
                HandleEnter(e, editor, viewModel, text, caretIndex, selectionLength);
                break;

            case Key.Back:
                HandleBackspace(e, editor, text, caretIndex, selectionLength, viewModel);
                break;

            case Key.Up:
                if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
                    HandleVerticalArrow(e, editor, up: true);
                break;

            case Key.Down:
                if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
                    HandleVerticalArrow(e, editor, up: false);
                break;

            case Key.Escape:
                e.Handled = true;
                EscapePressed?.Invoke();
                break;
        }
    }

    /// <summary>
    /// Owns Up/Down for <see cref="RichTextEditor"/> so <c>e.Handled</c> is always set; otherwise
    /// Avalonia's window-level directional navigation moves XY focus instead of editing.
    /// </summary>
    private void HandleVerticalArrow(KeyEventArgs e, RichTextEditor editor, bool up)
    {
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        e.Handled = true;
        if (!editor.TryVerticalLogicalNavigation(shift, up))
        {
            double? px = editor.TryGetCaretHorizontalOffsetForBlockNavigation(out var x) ? x : null;
            if (up)
                RequestFocusPrevious?.Invoke(px);
            else
                RequestFocusNext?.Invoke(px);
        }
    }

    private void HandleEnter(KeyEventArgs e, RichTextEditor editor, BlockViewModel viewModel, string text, int caretIndex, int selectionLength)
    {
        e.Handled = true;
        if (viewModel.Type == BlockType.Image)
        {
            editor.InsertTextAtCaret("\n");
            return;
        }
        // Split column: Enter inserts a newline in non-empty text; whitespace-only line exits (handled in EditableBlock).
        if (viewModel.OwnerTwoColumn != null)
        {
            if (selectionLength == 0 && QuoteEnterBehavior.IsCaretOnWhitespaceOnlyLine(text, caretIndex))
            {
                EnterPressed?.Invoke();
                return;
            }
            editor.InsertTextAtCaret("\n");
            return;
        }
        if (viewModel.Type == BlockType.Quote)
        {
            if (selectionLength == 0 && QuoteEnterBehavior.IsCaretOnWhitespaceOnlyLine(text, caretIndex))
            {
                EnterPressed?.Invoke();
                return;
            }

            editor.InsertTextAtCaret("\n");
            return;
        }

        var hasContent = !BlockEditorContentPolicy.IsVisuallyEmpty(editor.Text);
        if (viewModel.Type is BlockType.BulletList or BlockType.NumberedList or BlockType.Checklist)
        {
            if (hasContent)
                EnterPressed?.Invoke();
            else
                ConvertToBlockType?.Invoke(BlockType.Text);
        }
        else
        {
            EnterPressed?.Invoke();
        }
    }

    private void HandleBackspace(KeyEventArgs e, RichTextEditor editor, string text, int caretIndex, int selectionLength, BlockViewModel viewModel)
    {
        if (selectionLength != 0)
        {
            // RichTextEditor handles selection deletion internally; just mark handled
            // so the tunnel/bubble chain doesn't double-process.
            return;
        }

        if (caretIndex == 0)
        {
            var isEmpty = BlockEditorContentPolicy.IsVisuallyEmpty(text);
            if (isEmpty)
            {
                if (viewModel.Type == BlockType.Image)
                {
                    e.Handled = true;
                    viewModel.NotifyStructuralChangeStarting();
                    viewModel.RequestDeleteAndFocusAbove();
                    return;
                }
                e.Handled = true;
                BackspaceOnEmpty?.Invoke();
            }
            else if (viewModel.Type == BlockType.Image)
            {
                // Caption: backspace at start of non-empty text — do not merge into previous block.
                return;
            }
            else if (viewModel.Type != BlockType.Text)
            {
                e.Handled = true;
                ConvertToTextPreservingContent?.Invoke();
            }
            else
            {
                e.Handled = true;
                MergeWithPrevious?.Invoke();
            }
        }
    }

    public void HandleBackspaceOnEmptyBlock(BlockViewModel viewModel)
    {
        if (viewModel == null) return;
        if (viewModel.Type == BlockType.Text)
            viewModel.RequestDeleteAndFocusAbove();
        else
            ConvertToBlockType?.Invoke(BlockType.Text);
    }

    public bool IsSlashKeyPress(Key key, string text) =>
        text == "/" || key == Key.Divide || key == Key.OemQuestion || key == Key.Oem2;
}
