using Avalonia.Controls;
using Mnemo.Core.Models;
using System;
using System.Collections.Generic;

namespace Mnemo.UI.Components.BlockEditor;

public class MarkdownShortcutDetector
{
    private static readonly Dictionary<string, (BlockType Type, Dictionary<string, object>? Meta)> Shortcuts = new()
    {
        ["#"] = (BlockType.Heading1, null),
        ["##"] = (BlockType.Heading2, null),
        ["###"] = (BlockType.Heading3, null),
        ["-"] = (BlockType.BulletList, null),
        ["*"] = (BlockType.BulletList, null),
        ["[]"] = (BlockType.Checklist, null),
        ["[ ]"] = (BlockType.Checklist, null),
        [">"] = (BlockType.Quote, null),
        ["```"] = (BlockType.Code, new Dictionary<string, object> { ["language"] = "csharp" }),
        ["1."] = (BlockType.NumberedList, null)
    };

    public event Action<BlockType, Dictionary<string, object>?>? ShortcutDetected;

    public bool TryDetectShortcut(TextBox textBox)
    {
        if (textBox?.Text == null) return false;

        var text = textBox.Text.Trim();
        
        if (Shortcuts.TryGetValue(text, out var conversion))
        {
            ShortcutDetected?.Invoke(conversion.Type, conversion.Meta);
            return true;
        }

        return false;
    }

    public bool IsMarkdownShortcutKey(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && Shortcuts.ContainsKey(text.Trim());
    }
}


