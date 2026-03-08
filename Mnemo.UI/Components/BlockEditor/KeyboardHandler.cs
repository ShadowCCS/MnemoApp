using Avalonia.Input;
using Mnemo.Core.Models;
using System;
using System.Collections.Generic;

namespace Mnemo.UI.Components.BlockEditor;

public class KeyboardHandler
{
    private static readonly HashSet<BlockType> TextAndHeadingTypes = new()
    {
        BlockType.Text,
        BlockType.Heading1,
        BlockType.Heading2,
        BlockType.Heading3
    };

    private Key? _lastKey;
    public Key? LastKey { get => _lastKey; set => _lastKey = value; }
    public bool WasBackspace => _lastKey == Key.Back;

    public event Action? BackspaceOnEmpty;
    public event Action? RequestFocusPrevious;
    public event Action? RequestFocusNext;
#pragma warning disable CS0067
    public event Action? RequestNewBlock;
#pragma warning restore CS0067
    public event Action<BlockType>? RequestNewBlockOfType;
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
        var selectionLength = editor.SelectionEnd - editor.SelectionStart;

        switch (e.Key)
        {
            case Key.Enter when viewModel.Type != BlockType.Code:
                HandleEnter(e, editor, viewModel);
                break;

            case Key.Back:
                HandleBackspace(e, editor, text, caretIndex, selectionLength, viewModel);
                break;

            case Key.Up when caretIndex == 0 || IsOnFirstLine(editor):
                e.Handled = true;
                RequestFocusPrevious?.Invoke();
                break;

            case Key.Down when caretIndex == text.Length || IsOnLastLine(editor):
                e.Handled = true;
                RequestFocusNext?.Invoke();
                break;

            case Key.Escape:
                e.Handled = true;
                EscapePressed?.Invoke();
                break;
        }
    }

    private void HandleEnter(KeyEventArgs e, RichTextEditor editor, BlockViewModel viewModel)
    {
        e.Handled = true;
        var hasContent = !string.IsNullOrWhiteSpace(editor.Text);
        if (viewModel.Type is BlockType.BulletList or BlockType.NumberedList or BlockType.Checklist)
        {
            if (hasContent)
                RequestNewBlockOfType?.Invoke(viewModel.Type);
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
            var isEmpty = string.IsNullOrWhiteSpace(text);
            if (isEmpty)
            {
                e.Handled = true;
                BackspaceOnEmpty?.Invoke();
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

    private static bool IsOnFirstLine(RichTextEditor editor)
    {
        var text = editor.Text;
        var caretIndex = editor.CaretIndex;
        if (caretIndex == 0) return true;
        return !text[..caretIndex].Contains('\n');
    }

    private static bool IsOnLastLine(RichTextEditor editor)
    {
        var text = editor.Text;
        var caretIndex = editor.CaretIndex;
        if (caretIndex >= text.Length) return true;
        return !text[caretIndex..].Contains('\n');
    }
}
