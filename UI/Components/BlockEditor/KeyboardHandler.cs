using Avalonia.Controls;
using Avalonia.Input;
using MnemoApp.Modules.Notes.Models;
using System;
using System.Collections.Generic;

namespace MnemoApp.UI.Components.BlockEditor;

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
    public event Action? RequestNewBlock;
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

        switch (e.Key)
        {
            case Key.Enter when viewModel.Type != BlockType.Code:
                HandleEnter(e, textBox, viewModel);
                break;
                
            case Key.Back:
                HandleBackspace(e, textBox, text, caretIndex, viewModel);
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

    private void HandleBackspace(KeyEventArgs e, TextBox textBox, string text, int caretIndex, BlockViewModel viewModel)
    {
        var isTextBoxEmpty = string.IsNullOrWhiteSpace(text);
        var isContentEmpty = string.IsNullOrWhiteSpace(viewModel.Content);

        if ((isTextBoxEmpty || isContentEmpty) && (caretIndex <= BACKSPACE_THRESHOLD || text.Length == 0))
        {
            e.Handled = true;
            BackspaceOnEmpty?.Invoke();
        }
    }

    public void HandleBackspaceOnEmptyBlock(BlockViewModel viewModel)
    {
        if (viewModel == null) return;

        if (IsTextOrHeadingBlock(viewModel.Type))
        {
            // Delete block and focus above
            viewModel.RequestDeleteAndFocusAbove();
        }
        else
        {
            // Convert to text block
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

