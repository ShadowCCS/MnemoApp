using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Clipboard;

using Mnemo.Infrastructure.Services.Notes.Markdown;

namespace Mnemo.UI.Components.BlockEditor;

public static class NoteClipboardMapper
{
    public static NoteClipboardDocument ToDocument(IEnumerable<BlockViewModel> blocks)
    {
        var list = new List<NoteClipboardBlockDto>();
        foreach (var vm in blocks)
            list.Add(ToDto(vm));

        return new NoteClipboardDocument { SchemaVersion = 1, Blocks = list };
    }

    public static List<BlockViewModel> ToViewModels(NoteClipboardDocument document, int firstOrder = 0)
    {
        var list = new List<BlockViewModel>();
        int order = firstOrder;
        foreach (var dto in document.Blocks)
        {
            list.Add(ToViewModel(dto, order++));
        }

        return list;
    }

    private static NoteClipboardBlockDto ToDto(BlockViewModel vm)
    {
        var dto = new NoteClipboardBlockDto
        {
            Type = vm.Type,
            Runs = vm.Runs.Select(ToRunDto).ToList(),
            ListNumberIndex = vm.Type == BlockType.NumberedList ? vm.ListNumberIndex : null
        };
        if (vm.Type == BlockType.Checklist)
            dto.IsChecked = vm.IsChecked;
        if (vm.Type == BlockType.Code &&
            vm.Meta.TryGetValue("language", out var lang) &&
            lang != null)
            dto.CodeLanguage = lang.ToString();

        if (vm.Type == BlockType.Image)
        {
            dto.ImagePath = MetaString(vm, "imagePath");
            dto.ImageAlt = MetaString(vm, "imageAlt");
            if (vm.Meta.TryGetValue("imageWidth", out var w) && w != null)
            {
                dto.ImageWidth = w switch
                {
                    double d => d,
                    System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Number => je.GetDouble(),
                    _ => double.TryParse(w.ToString(), out var p) ? p : null
                };
            }
            var ia = MetaString(vm, "imageAlign");
            if (!string.IsNullOrEmpty(ia))
                dto.ImageAlign = ia;
        }

        dto.Content = vm.Content;
        return dto;
    }

    private static NoteClipboardRunDto ToRunDto(InlineRun run) =>
        new()
        {
            Text = run.Text,
            Bold = run.Style.Bold,
            Italic = run.Style.Italic,
            Underline = run.Style.Underline,
            Strikethrough = run.Style.Strikethrough,
            Code = run.Style.Code,
            BackgroundColor = run.Style.BackgroundColor
        };

    private static BlockViewModel ToViewModel(NoteClipboardBlockDto dto, int order)
    {
        var vm = BlockFactory.CreateBlock(dto.Type, order);
        if (!string.IsNullOrEmpty(dto.CodeLanguage))
            vm.Meta["language"] = dto.CodeLanguage;

        if (dto.Type == BlockType.Checklist && dto.IsChecked.HasValue)
            vm.IsChecked = dto.IsChecked.Value;

        if (dto.Type == BlockType.NumberedList && dto.ListNumberIndex is { } n)
            vm.ListNumberIndex = n;

        if (dto.Type == BlockType.Image)
        {
            vm.Meta["imagePath"] = dto.ImagePath ?? string.Empty;
            vm.Meta["imageAlt"] = dto.ImageAlt ?? string.Empty;
            vm.Meta["imageWidth"] = dto.ImageWidth is > 0 ? dto.ImageWidth.Value : 0.0;
            if (!string.IsNullOrEmpty(dto.ImageAlign))
                vm.Meta["imageAlign"] = dto.ImageAlign;
            vm.SetRuns(new List<InlineRun> { InlineRun.Plain(string.Empty) });
            return vm;
        }

        if (dto.Runs is { Count: > 0 })
        {
            var runs = dto.Runs.Select(FromRunDto).ToList();
            vm.SetRuns(InlineRunFormatApplier.Normalize(runs));
        }
        else if (!string.IsNullOrEmpty(dto.Content))
            vm.SetRuns(InlineMarkdownParser.ToRuns(dto.Content));
        else
            vm.SetRuns(new List<InlineRun> { InlineRun.Plain(string.Empty) });

        return vm;
    }

    private static string MetaString(BlockViewModel vm, string key)
    {
        if (!vm.Meta.TryGetValue(key, out var val) || val == null) return string.Empty;
        if (val is string s) return s;
        if (val is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return val.ToString() ?? string.Empty;
    }

    private static InlineRun FromRunDto(NoteClipboardRunDto dto)
    {
        var style = new InlineStyle(
            Bold: dto.Bold,
            Italic: dto.Italic,
            Underline: dto.Underline,
            Strikethrough: dto.Strikethrough,
            Code: dto.Code,
            BackgroundColor: dto.BackgroundColor);
        return new InlineRun(dto.Text ?? string.Empty, style);
    }
}
