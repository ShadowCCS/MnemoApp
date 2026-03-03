using Avalonia.Controls;
using Avalonia.Input;
using Mnemo.Core.Models;
using System;
using System.Collections.Generic;

namespace Mnemo.UI.Components.BlockEditor;

public class KeyboardHandler
{
    private const int BACKSPACE_THRESHOLD = 1;
    
    private static readonly HashSet<BlockType> TextAndHeadingTypes = new()
    {
        BlockType.Text,
        BlockType.Heading1,
        BlockType.Heading2,
        BlockType.Heading3
    };

    private Key? _lastKey;

    public Key? LastKey
    {
        get => _lastKey;
        set => _lastKey = value;
    }

    public bool WasBackspace => _lastKey == Key.Back;

    public event Action? BackspaceOnEmpty;
    public event Action? RequestFocusPrevious;
    public event Action? RequestFocusNext;
    #pragma warning disable CS0067 // Event is never used
    public event Action? RequestNewBlock;
    #pragma warning restore CS0067
    public event Action<BlockType>? RequestNewBlockOfType;
    public event Action<BlockType>? ConvertToBlockType;
    public event Action? ConvertToTextPreservingContent;
    public event Action? EscapePressed;
    public event Action? EnterPressed;
    public event Action? MergeWithPrevious;

    public void HandleKeyDown(KeyEventArgs e, TextBox textBox, BlockViewModel? viewModel)
    {
        if (e.Handled || viewModel == null || textBox == null) return;

        LastKey = e.Key;
        var text = textBox.Text ?? string.Empty;
        var caretIndex = textBox.CaretIndex;
        var selectionLength = textBox.SelectionEnd - textBox.SelectionStart;

        switch (e.Key)
        {
            case Key.Enter when viewModel.Type != BlockType.Code:
                HandleEnter(e, textBox, viewModel);
                break;
                
            case Key.Back:
                HandleBackspace(e, textBox, text, caretIndex, selectionLength, viewModel);
                break;
                
            case Key.Up when caretIndex == 0 || IsOnFirstLine(textBox):
                e.Handled = true;
                RequestFocusPrevious?.Invoke();
                break;
                
            case Key.Down when caretIndex == text.Length || IsOnLastLine(textBox):
                e.Handled = true;
                RequestFocusNext?.Invoke();
                break;
                
            case Key.Escape:
                e.Handled = true;
                EscapePressed?.Invoke();
                break;
                
            case Key.Space:
                // Markdown shortcuts are handled separately
                break;
        }
    }

    private void HandleEnter(KeyEventArgs e, TextBox textBox, BlockViewModel viewModel)
    {
        e.Handled = true;
        
        var hasContent = !string.IsNullOrWhiteSpace(textBox.Text);

        if (viewModel.Type is BlockType.BulletList or BlockType.NumberedList or BlockType.Checklist)
        {
            if (hasContent)
            {
                RequestNewBlockOfType?.Invoke(viewModel.Type);
            }
            else
            {
                ConvertToBlockType?.Invoke(BlockType.Text);
            }
        }
        else
        {
            EnterPressed?.Invoke();
        }
    }

    private void HandleBackspace(KeyEventArgs e, TextBox textBox, string text, int caretIndex, int selectionLength, BlockViewModel viewModel)
    {
        System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] HandleBackspace - Text: '{text}', CaretIndex: {caretIndex}, SelectionLength: {selectionLength}, BlockType: {viewModel.Type}");

        // If there's a selection, explicitly delete it (Avalonia TextBox may not handle Backspace-for-selection reliably when custom handlers are present)
        if (selectionLength != 0)
        {
            int start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
            int len = Math.Abs(textBox.SelectionEnd - textBox.SelectionStart);
            if (len > 0 && start >= 0 && start + len <= text.Length)
            {
                string newText = text.Remove(start, len);
                textBox.Text = newText;
                textBox.CaretIndex = start;
                textBox.SelectionStart = start;
                textBox.SelectionEnd = start;
                viewModel.Content = newText;
                e.Handled = true;
            }
            return;
        }

        if (caretIndex == 0)
        {
            // Use the live TextBox text — viewModel.Content may not have synced yet at KeyDown time
            var isEmpty = string.IsNullOrWhiteSpace(text);

            System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] At caret 0 - IsEmpty: {isEmpty}, BlockType: {viewModel.Type}");

            if (isEmpty)
            {
                System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] Triggering BackspaceOnEmpty event");
                e.Handled = true;
                BackspaceOnEmpty?.Invoke();
            }
            else if (viewModel.Type != BlockType.Text)
            {
                // Non-text block with content at position 0 → convert to Text, preserving content
                System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] Non-text block with content at position 0 - converting to Text (preserving content)");
                e.Handled = true;
                ConvertToTextPreservingContent?.Invoke();
            }
            else
            {
                // Text block with content at position 0 → merge with the block above
                System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] Text block with content at position 0 - merging with previous");
                e.Handled = true;
                MergeWithPrevious?.Invoke();
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] Caret not at 0, letting TextBox handle");
        }
    }

    public void HandleBackspaceOnEmptyBlock(BlockViewModel viewModel)
    {
        System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] HandleBackspaceOnEmptyBlock - BlockType: {viewModel?.Type}");
        if (viewModel == null) return;

        if (viewModel.Type == BlockType.Text)
        {
            // Empty text block → delete it and focus above
            System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] Text block - requesting delete and focus above");
            viewModel.RequestDeleteAndFocusAbove();
        }
        else
        {
            // Empty non-text block → convert to text block
            System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] Non-text block - converting to Text");
            ConvertToBlockType?.Invoke(BlockType.Text);
        }
    }

    public bool IsSlashKeyPress(Key key, string text)
    {
        return text == "/" || 
               key == Key.Divide || 
               key == Key.OemQuestion || 
               key == Key.Oem2;
    }

    private bool IsTextOrHeadingBlock(BlockType type) => TextAndHeadingTypes.Contains(type);

    private bool IsOnFirstLine(TextBox textBox)
    {
        if (!textBox.AcceptsReturn || textBox.CaretIndex == 0)
            return true;

        var text = textBox.Text ?? string.Empty;
        return !text[..textBox.CaretIndex].Contains('\n');
    }

    private bool IsOnLastLine(TextBox textBox)
    {
        if (!textBox.AcceptsReturn || textBox.CaretIndex == (textBox.Text?.Length ?? 0))
            return true;

        var text = textBox.Text ?? string.Empty;
        return !text[textBox.CaretIndex..].Contains('\n');
    }
}


