using System;
using System.Linq;
using Mnemo.Core.Models;

namespace Mnemo.UI.Modules.Notes;

/// <summary>Plain-text extraction for AI tool handlers (read/update/append).</summary>
internal static class NoteToolContentHelper
{
    public static string GetPlainText(Note note)
    {
        if (note.Blocks is { Count: > 0 })
        {
            foreach (var b in note.Blocks)
                b.EnsureInlineRuns();
            return string.Join("\n\n", note.Blocks.OrderBy(b => b.Order).Select(b => b.Content));
        }

        return note.Content ?? string.Empty;
    }

    public static void SetBodyAsSingleTextBlock(Note note, string body)
    {
        note.Blocks =
        [
            new Block
            {
                Id = Guid.NewGuid().ToString(),
                Type = BlockType.Text,
                Content = body,
                Order = 0
            }
        ];
        note.Content = string.Empty;
    }
}
