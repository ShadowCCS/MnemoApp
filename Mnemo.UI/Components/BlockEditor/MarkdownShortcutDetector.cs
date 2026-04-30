using Mnemo.Core.Models;
using System;
using System.Collections.Generic;

namespace Mnemo.UI.Components.BlockEditor;

public class MarkdownShortcutDetector
{
    /// <summary>Initial plain text after stripping a markdown line prefix; not stored in <see cref="BlockViewModel.Meta"/>.</summary>
    public const string ShortcutReplacementContentKey = "__shortcutContent";

    private static readonly Dictionary<string, (BlockType Type, Dictionary<string, object>? Meta)> Shortcuts = new()
    {
        ["#"] = (BlockType.Heading1, null),
        ["##"] = (BlockType.Heading2, null),
        ["###"] = (BlockType.Heading3, null),
        ["####"] = (BlockType.Heading4, null),
        ["-"] = (BlockType.BulletList, null),
        ["*"] = (BlockType.BulletList, null),
        ["[]"] = (BlockType.Checklist, null),
        ["[ ]"] = (BlockType.Checklist, null),
        [">"] = (BlockType.Quote, null),
        ["---"] = (BlockType.Divider, null),
        ["```"] = (BlockType.Code, new Dictionary<string, object> { ["language"] = "csharp" }),
        ["1."] = (BlockType.NumberedList, null)
    };

    public event Action<BlockType, Dictionary<string, object>?>? ShortcutDetected;

    /// <param name="blockType">Current block; leading <c>* </c>/<c>- </c>/<c>+ </c> → list only applies to <see cref="BlockType.Text"/>.</param>
    public bool TryDetectShortcut(RichTextEditor editor, BlockType blockType)
    {
        if (editor == null) return false;
        var text = editor.Text;

        // CommonMark list markers at block start, remainder becomes list item body (e.g. "* " + existing text).
        if (blockType == BlockType.Text && TryMatchLeadingBulletMarker(text, out var afterMarker))
        {
            var meta = new Dictionary<string, object> { [ShortcutReplacementContentKey] = afterMarker };
            ShortcutDetected?.Invoke(BlockType.BulletList, meta);
            return true;
        }

        var trimmed = text.Trim();
        if (Shortcuts.TryGetValue(trimmed, out var conversion))
        {
            ShortcutDetected?.Invoke(conversion.Type, conversion.Meta);
            return true;
        }
        return false;
    }

    private static bool TryMatchLeadingBulletMarker(string text, out string remainder)
    {
        remainder = string.Empty;
        if (text.Length < 2) return false;
        var c0 = text[0];
        if (c0 is not ('*' or '-' or '+')) return false;
        if (text[1] is not (' ' or '\t')) return false;
        remainder = text.Length > 2 ? text[2..] : string.Empty;
        return true;
    }

    public bool IsMarkdownShortcutKey(string text) =>
        !string.IsNullOrWhiteSpace(text) && Shortcuts.ContainsKey(text.Trim());
}
