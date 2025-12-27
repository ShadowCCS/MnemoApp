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
    public event Action? EscapePressed;
    public event Action? EnterPressed;

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
        
        // If there's a selection, backspace will delete it - let TextBox handle that normally
        if (selectionLength > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] Selection detected, letting TextBox handle");
            return;
        }

        // When caret is at position 0 with no selection, backspace can't delete anything before it
        // So we check if the block is already empty/whitespace
        if (caretIndex == 0)
        {
            var isEmpty = string.IsNullOrWhiteSpace(text);
            // Also check ViewModel content in case TextBox hasn't synced yet
            var isContentEmpty = string.IsNullOrWhiteSpace(viewModel.Content);
            
            System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] At caret 0 - IsEmpty: {isEmpty}, IsContentEmpty: {isContentEmpty}");
            
            if (isEmpty || isContentEmpty)
            {
                System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] Triggering BackspaceOnEmpty event");
                e.Handled = true;
                BackspaceOnEmpty?.Invoke();
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
            // If it's already a text block and empty, delete it and focus above
            System.Diagnostics.Debug.WriteLine($"[KeyboardHandler] Text block - requesting delete and focus above");
            viewModel.RequestDeleteAndFocusAbove();
        }
        else
        {
            // For any special block (non-text), convert to text block first
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


